using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services.Music;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Ai;

/// <summary>A registered light, flattened for AI-tool context (key/name/provider only).</summary>
public record LightInfo(string Key, string Name, string Provider);

/// <summary>A sound-effect summary for AI-tool context (no waveform, no file name).</summary>
public record SoundInfo(string Id, string Name, string Category, double Volume, bool Loop, int? DurationMs);

/// <summary>The scene the table is currently showing (null id = none activated yet).</summary>
public record ActiveSceneInfo(string? Id, DateTimeOffset? ActivatedAt);

/// <summary>The running light-FX test (its FX id and the clamped window in seconds).</summary>
public record LightFxTestInfo(string Testing, int Seconds);

/// <summary>What the player-facing /tv display is currently showing, slimmed for AI-tool context: the live
/// revision plus the pushed content (kind "image"/"board"/"encounter", its ref and label; all null when the
/// display is cleared).</summary>
public record TvStatusInfo(long Rev, string? Kind, string? Ref, string? Label);

/// <summary>
/// Shared tool layer over scenes, events, screens and light FX (plus read-only context and live control —
/// Spotify music transport, soundboard playback, and lights), used by both the MCP server and the in-panel
/// assistant so the two surfaces behave identically to each other and to the HTTP endpoints they mirror. CRUD
/// goes straight to the stores + existing validators + image cleanup, exactly like the endpoints. Registered
/// as a singleton, but <see cref="SceneActivator"/> / <see cref="EventActivator"/> / <see cref="ILightService"/>
/// / <see cref="SpotifyClient"/> are scoped (the light provider is chosen per-scope from settings), so each
/// activation/trigger/reset/Spotify/lights-status call creates its own service scope for the operation (the
/// pattern used by <see cref="EventTimelineRunner"/>); <see cref="LightFxTester"/> owns its own scope per test
/// run. The soundboard (<see cref="ISoundboardPlayer"/>) is a singleton, so live sound control needs no scope.
/// </summary>
public sealed class AiToolService(
    IServiceScopeFactory scopeFactory,
    SceneStore scenes,
    EventStore events,
    ScreenStore screens,
    LightFxStore lightFx,
    SoundStore sounds,
    SettingsStore settings,
    LightRegistry lights,
    CurrentState state,
    EventTimelineRunner timelineRunner,
    LightFxTester fxTester,
    ImageFileStorage images,
    EffectEngine effects,
    ISoundboardPlayer player,
    SoundFileStorage soundFiles,
    PartyStore party,
    EncounterStore encounters,
    BoardStore boards,
    TvState tvState)
{
    // ---- Scenes ----

    public Task<List<Scene>> ListScenesAsync() => scenes.GetAllAsync();

    public Task<Scene?> GetSceneAsync(string id) => scenes.GetAsync(id);

    // Mirrors PUT /scenes/{id}: stamp the id, validate, then upsert and drop replaced/cleared tile art.
    public async Task<Scene> UpsertSceneAsync(Scene scene, string id)
    {
        scene.Id = id;
        SceneValidation.Validate(scene);
        var oldImage = (await scenes.GetAsync(id))?.Image;
        await scenes.UpsertAsync(scene);
        if (!string.IsNullOrEmpty(oldImage) && !string.Equals(oldImage, scene.Image, StringComparison.OrdinalIgnoreCase))
            images.Delete(oldImage);
        return scene;
    }

    // Mirrors DELETE /scenes/{id}: remove the tile art too, and scrub every dangling reference (shortcut
    // tiles, event After, the active-scene highlight). False when the scene didn't exist.
    public async Task<bool> DeleteSceneAsync(string id)
    {
        var image = (await scenes.GetAsync(id))?.Image;
        if (!await scenes.DeleteAsync(id)) return false;
        images.Delete(image);
        await ReferenceScrubber.ScrubScreenTilesAsync(screens, "scene", id);
        await ReferenceScrubber.ScrubEventAfterSceneAsync(events, id);
        if (string.Equals(state.ActiveSceneId, id, StringComparison.OrdinalIgnoreCase))
        {
            state.ActiveSceneId = null;
            state.ActivatedAt = null;
        }
        return true;
    }

    public async Task<ActivationResult> ActivateSceneAsync(string id)
    {
        if (await scenes.GetAsync(id) is not { } scene)
            throw new ArgumentException($"Unknown scene '{id}'.");
        // SceneActivator (and the light provider it uses) is scoped, so activate inside a fresh scope.
        using var scope = scopeFactory.CreateScope();
        var activator = scope.ServiceProvider.GetRequiredService<SceneActivator>();
        return await activator.ActivateAsync(scene);
    }

    public ActiveSceneInfo GetActiveScene() => new(state.ActiveSceneId, state.ActivatedAt);

    // ---- Events ----

    public Task<List<GameEvent>> ListEventsAsync() => events.GetAllAsync();

    public Task<GameEvent?> GetEventAsync(string id) => events.GetAsync(id);

    // Mirrors PUT /events/{id}: stamp the id, validate, then upsert and drop replaced/cleared tile art.
    public async Task<GameEvent> UpsertEventAsync(GameEvent evt, string id)
    {
        evt.Id = id;
        EventValidation.Validate(evt);
        var oldImage = (await events.GetAsync(id))?.Image;
        await events.UpsertAsync(evt);
        if (!string.IsNullOrEmpty(oldImage) && !string.Equals(oldImage, evt.Image, StringComparison.OrdinalIgnoreCase))
            images.Delete(oldImage);
        return evt;
    }

    // Mirrors DELETE /events/{id}: remove the tile art too, and drop any shortcut tile that pointed at it.
    // False when the event didn't exist.
    public async Task<bool> DeleteEventAsync(string id)
    {
        var image = (await events.GetAsync(id))?.Image;
        if (!await events.DeleteAsync(id)) return false;
        images.Delete(image);
        await ReferenceScrubber.ScrubScreenTilesAsync(screens, "event", id);
        return true;
    }

    public async Task<EventResult> TriggerEventAsync(string id)
    {
        if (await events.GetAsync(id) is not { } evt)
            throw new ArgumentException($"Unknown event '{id}'.");
        // EventActivator (and the light provider it uses) is scoped, so trigger inside a fresh scope.
        using var scope = scopeFactory.CreateScope();
        var activator = scope.ServiceProvider.GetRequiredService<EventActivator>();
        return await activator.TriggerAsync(evt);
    }

    // Stop the running event timeline (if any). Returns whether one was running.
    public bool StopEvent() => timelineRunner.Stop();

    // Mirrors GET /events/state: the id of the event whose timeline is currently running, or null.
    public EventStateDto GetEventState() => new(timelineRunner.RunningEventId);

    // ---- Screens ----

    public Task<List<Screen>> ListScreensAsync() => screens.GetAllAsync();

    // There is deliberately no GET /screens/{id} endpoint; read straight from the store (the panel does the
    // same, picking a board out of /screens/list client-side).
    public Task<Screen?> GetScreenAsync(string id) => screens.GetAsync(id);

    // Mirrors PUT /screens/{id}: stamp the id, validate, then upsert and drop replaced/cleared tile art.
    public async Task<Screen> UpsertScreenAsync(Screen screen, string id)
    {
        screen.Id = id;
        ScreenValidation.Validate(screen);
        var oldImage = (await screens.GetAsync(id))?.Image;
        await screens.UpsertAsync(screen);
        if (!string.IsNullOrEmpty(oldImage) && !string.Equals(oldImage, screen.Image, StringComparison.OrdinalIgnoreCase))
            images.Delete(oldImage);
        return screen;
    }

    // Mirrors DELETE /screens/{id}: remove the tile art too. Nothing references a screen (it is the thing that
    // references others), so unlike scene/event delete there is nothing to scrub. False when it didn't exist.
    public async Task<bool> DeleteScreenAsync(string id)
    {
        var image = (await screens.GetAsync(id))?.Image;
        if (!await screens.DeleteAsync(id)) return false;
        images.Delete(image);
        return true;
    }

    // ---- Light FX ----

    public Task<List<LightFx>> ListLightFxAsync() => lightFx.GetAllAsync();

    public Task<LightFx?> GetLightFxAsync(string id) => lightFx.GetAsync(id);

    // Mirrors PUT /lightfx/{id}: stamp the id, validate, upsert.
    public async Task<LightFx> UpsertLightFxAsync(LightFx fx, string id)
    {
        fx.Id = id;
        LightFxValidation.Validate(fx);
        await lightFx.UpsertAsync(fx);
        return fx;
    }

    // Mirrors DELETE /lightfx/{id}: detach every live reference into an embedded "custom" copy first, then
    // delete. False when the FX didn't exist.
    public async Task<bool> DeleteLightFxAsync(string id)
    {
        if (await lightFx.GetAsync(id) is not { } effect) return false;
        await LightFxDetacher.DetachReferencesAsync(effect, scenes, events);
        await lightFx.DeleteAsync(id);
        return true;
    }

    // Bounded preview of an FX on a light (null/empty key = the configured provider group), clamped 1–60 s.
    public async Task<LightFxTestInfo> TestLightFxAsync(string id, string? lightKey, int seconds = 10)
    {
        if (await lightFx.GetAsync(id) is not { } effect)
            throw new ArgumentException($"Unknown light FX '{id}'.");
        var window = Math.Clamp(seconds, 1, 60);
        // LightFxTester owns its own scope for the run; the base state is applied synchronously here so an
        // unconfigured/unreachable light throws instead of failing silently in the background loop.
        await fxTester.StartAsync(effect, string.IsNullOrWhiteSpace(lightKey) ? null : lightKey, window);
        return new LightFxTestInfo(effect.Id, window);
    }

    public bool StopLightFxTest() => fxTester.Stop();

    // ---- Sounds (live control + metadata) ----

    // Mirrors PUT /sounds/{id} for the AI-editable fields only (name/category/volume/loop); each null arg is
    // left unchanged. Tile art is deliberately NOT touched here (the AI can't manage image uploads), unlike
    // the endpoint which sets Image as sent. Returns the updated sound as the slim context record.
    public async Task<SoundInfo> UpdateSoundAsync(string id, string? name, string? category, double? volume, bool? loop)
    {
        if (await sounds.GetAsync(id) is not { } sound)
            throw new ArgumentException($"Unknown sound '{id}'.");
        if (name is not null) sound.Name = name.Trim();
        if (category is not null) sound.Category = category.Trim();
        if (volume is { } v) sound.Volume = v;
        if (loop is { } l) sound.Loop = l;
        SoundValidation.Validate(sound);
        await sounds.UpsertAsync(sound);
        return new SoundInfo(sound.Id, sound.Name, sound.Category, sound.Volume, sound.Loop, sound.DurationMs);
    }

    // Mirrors /sounds/{id}/play: overlay one soundboard sound on the server's audio device (volume defaults to
    // the sound's stored level). SoundboardPlayer is a singleton, so no scope is needed.
    public async Task<object> PlaySoundAsync(string id, double? volume)
    {
        if (await sounds.GetAsync(id) is not { } sound)
            throw new ArgumentException($"Unknown sound '{id}'.");
        player.Play(sound.Id, soundFiles.FullPath(sound), sound.Loop, volume ?? sound.Volume);
        return new { playing = sound.Id };
    }

    // Mirrors /sounds/{id}/stop: stop every voice playing this sound id (a no-op if it isn't playing).
    public object StopSound(string id)
    {
        player.Stop(id);
        return new { stopped = id };
    }

    // Mirrors /sounds/stop: stop all soundboard playback.
    public object StopAllSounds()
    {
        player.StopAll();
        return new { stopped = "all" };
    }

    // Mirrors GET /sounds/state: the ids of the sounds currently playing on the server.
    public SoundStateDto GetSoundsState() => new(player.PlayingIds);

    // ---- Music (source-routed transport) ----

    // Mirrors /music/play: play a spotify: URI/link OR a local:track:{id} / local:playlist:{id}; the router
    // infers the source from the id shape (and remembers it as active). Errors on an unrecognized id.
    public Task<object> PlayMusicAsync(string uri) =>
        WithMusicAsync<object>(async r => { var source = await r.PlayAsync(uri); return new { playing = uri, source }; });

    public Task<object> PauseMusicAsync() =>
        WithMusicAsync<object>(async r => { await r.Resolve().PauseAsync(); return new { music = "paused" }; });

    public Task<object> ResumeMusicAsync() =>
        WithMusicAsync<object>(async r => { await r.Resolve().ResumeAsync(); return new { music = "playing" }; });

    public Task<object> NextTrackAsync() =>
        WithMusicAsync<object>(async r => { await r.Resolve().NextAsync(); return new { music = "next" }; });

    public Task<object> PreviousTrackAsync() =>
        WithMusicAsync<object>(async r => { await r.Resolve().PreviousAsync(); return new { music = "previous" }; });

    public Task<object> SetMusicVolumeAsync(double value) =>
        WithMusicAsync<object>(async r => { await r.Resolve().SetVolumeAsync(value); return new { volume = value }; });

    public Task<object> SetMusicShuffleAsync(bool enabled) =>
        WithMusicAsync<object>(async r => { await r.Resolve().SetShuffleAsync(enabled); return new { shuffle = enabled }; });

    // Mirrors /music/repeat: off | track | playlist (Spotify maps "playlist" to its "context").
    public Task<object> SetMusicRepeatAsync(string mode)
    {
        if (mode is not ("off" or "track" or "playlist"))
            throw new ArgumentException("Repeat mode must be one of: off, track, playlist.");
        return WithMusicAsync<object>(async r => { await r.Resolve().SetRepeatAsync(mode); return new { repeat = mode }; });
    }

    // Mirrors GET /music/state: neutral playback state + which source produced it + the available sources.
    public Task<MusicStateDto> GetMusicStateAsync() => WithMusicAsync(r => r.GetStateAsync(null));

    // ---- Party (players + table-level counters) ----

    // Mirrors GET /party/list: the whole live table — players, table-level counters, and the bestiary enemies.
    public async Task<PartyDto> ListPartyAsync() =>
        new(await party.GetMembersAsync(), await party.GetTableCountersAsync(), await party.GetEnemiesAsync());

    // Mirrors PUT /party/players/{id}: stamp the id, validate, upsert, drop a replaced portrait, and refresh a
    // shown live roster (a board with a party/enemies element, or a shown encounter whose hero panel resolves
    // live from the party).
    public async Task<PartyMember> UpsertPlayerAsync(PartyMember member, string id)
    {
        member.Id = id;
        PartyValidation.Validate(member);
        var oldPortrait = (await party.GetMemberAsync(id))?.Portrait;
        await party.UpsertMemberAsync(member);
        if (!string.IsNullOrEmpty(oldPortrait) && !string.Equals(oldPortrait, member.Portrait, StringComparison.OrdinalIgnoreCase))
            images.Delete(oldPortrait);
        await TouchIfPartyShownAsync(heroChange: true);
        return member;
    }

    // Mirrors DELETE /party/players/{id}: release the portrait, refresh a shown live roster. False if it didn't exist.
    public async Task<bool> DeletePlayerAsync(string id)
    {
        var portrait = (await party.GetMemberAsync(id))?.Portrait;
        if (!await party.DeleteMemberAsync(id)) return false;
        images.Delete(portrait);
        await TouchIfPartyShownAsync(heroChange: true);
        return true;
    }

    // Mirrors PUT /party/counters: validate+normalize the table-level counter list in place, save, refresh the TV.
    public async Task<List<PartyCounter>> SaveTableCountersAsync(List<PartyCounter> counters)
    {
        counters ??= [];
        PartyValidation.ValidateCounters(counters);
        await party.SaveTableCountersAsync(counters);
        await TouchIfPartyShownAsync(heroChange: true);
        return counters;
    }

    // Mirrors /party/players/{id}/adjust: bump one counter by delta OR set it to value (exactly one), clamped.
    public async Task<PartyMember> AdjustPlayerCounterAsync(string id, string counter, int? delta, int? value)
    {
        AdjustGuard(counter, delta, value);
        var updated = await party.AdjustMemberCounterAsync(id, counter.Trim(), delta, value);
        await TouchIfPartyShownAsync(heroChange: true);
        return updated;
    }

    // Mirrors /party/counters/adjust: bump/set one table-level counter (Fear etc.), clamped.
    public async Task<List<PartyCounter>> AdjustTableCounterAsync(string counter, int? delta, int? value)
    {
        AdjustGuard(counter, delta, value);
        var updated = await party.AdjustTableCounterAsync(counter.Trim(), delta, value);
        await TouchIfPartyShownAsync(heroChange: true);
        return updated;
    }

    // ---- Bestiary enemies (reusable statblocks) ----

    // Mirrors PUT /party/enemies/{id}: the enemy twin of UpsertPlayerAsync. An enemy-template edit does NOT
    // touch a shown encounter (its instances are add-time snapshots — issue #122), so heroChange stays false;
    // it still refreshes a shown board with an enemies element (which renders the bestiary).
    public async Task<Enemy> UpsertEnemyAsync(Enemy enemy, string id)
    {
        enemy.Id = id;
        PartyValidation.Validate(enemy);
        var oldPortrait = (await party.GetEnemyAsync(id))?.Portrait;
        await party.UpsertEnemyAsync(enemy);
        if (!string.IsNullOrEmpty(oldPortrait) && !string.Equals(oldPortrait, enemy.Portrait, StringComparison.OrdinalIgnoreCase))
            images.Delete(oldPortrait);
        await TouchIfPartyShownAsync(heroChange: false);
        return enemy;
    }

    // Mirrors DELETE /party/enemies/{id}: release the template's portrait. False if it didn't exist.
    public async Task<bool> DeleteEnemyAsync(string id)
    {
        var portrait = (await party.GetEnemyAsync(id))?.Portrait;
        if (!await party.DeleteEnemyAsync(id)) return false;
        images.Delete(portrait);
        await TouchIfPartyShownAsync(heroChange: false);
        return true;
    }

    // Mirrors /party/enemies/{id}/adjust: bump/set one of a bestiary template's base counters, clamped.
    public async Task<Enemy> AdjustEnemyCounterAsync(string id, string counter, int? delta, int? value)
    {
        AdjustGuard(counter, delta, value);
        var updated = await party.AdjustEnemyCounterAsync(id, counter.Trim(), delta, value);
        await TouchIfPartyShownAsync(heroChange: false);
        return updated;
    }

    // ---- Encounters (prepped fights: heroes + enemy instances + background + optional scene/event) ----

    public Task<List<Encounter>> ListEncountersAsync() => encounters.GetAllAsync();

    // There is deliberately no GET /encounters/{id} endpoint; read straight from the store (the GetScreen idiom).
    public Task<Encounter?> GetEncounterAsync(string id) => encounters.GetAsync(id);

    // Mirrors PUT /encounters/{id}: stamp the id, validate, upsert, drop a replaced background image, and
    // refresh the TV if this encounter is the one being shown. Instance portraits are template snapshots (owned
    // in the bestiary), so they are deliberately not released here.
    public async Task<Encounter> UpsertEncounterAsync(Encounter encounter, string id)
    {
        encounter.Id = id;
        EncounterValidation.Validate(encounter);
        var oldBackground = (await encounters.GetAsync(id))?.BackgroundImage;
        await encounters.UpsertAsync(encounter);
        if (!string.IsNullOrEmpty(oldBackground) && !string.Equals(oldBackground, encounter.BackgroundImage, StringComparison.OrdinalIgnoreCase))
            images.Delete(oldBackground);
        tvState.TouchEncounter(id);
        return encounter;
    }

    // Mirrors DELETE /encounters/{id}: release the owned background, then clear+scrub the display if it was showing.
    public async Task<bool> DeleteEncounterAsync(string id)
    {
        var background = (await encounters.GetAsync(id))?.BackgroundImage;
        if (!await encounters.DeleteAsync(id)) return false;
        images.Delete(background);
        tvState.ForgetEncounter(id);
        return true;
    }

    // Mirrors /encounters/{id}/run: activate the encounter's scene + event best-effort (each reports
    // ok/skipped/notFound/partial/error but never blocks the show), then push the heroes-left / enemies-right
    // view to the TV. Throws NotFoundException if the encounter id is unknown.
    public async Task<object> RunEncounterAsync(string id)
    {
        var encounter = await encounters.GetAsync(id)
            ?? throw new NotFoundException("error.encounter.notFound", id);
        var sceneStatus = await RunSceneBestEffortAsync(encounter.ActivateSceneId);
        var eventStatus = await RunEventBestEffortAsync(encounter.ActivateEventId);
        var rev = tvState.ShowEncounter(encounter.Id, encounter.Name);
        return new { rev, encounter = encounter.Id, scene = sceneStatus, @event = eventStatus };
    }

    // Mirrors /encounters/{id}/enemies/{instanceId}/adjust: bump/set one enemy instance's live counter, clamped.
    public async Task<Encounter> AdjustEncounterEnemyAsync(string id, string instanceId, string counter, int? delta, int? value)
    {
        AdjustGuard(counter, delta, value);
        var updated = await encounters.AdjustEnemyInstanceAsync(id, instanceId, counter.Trim(), delta, value);
        tvState.TouchEncounter(id);
        return updated;
    }

    // Mirrors /encounters/{id}/reset: reset every enemy instance's counters to its statblock's starting values.
    public async Task<Encounter> ResetEncounterAsync(string id)
    {
        var updated = await encounters.ResetEnemiesAsync(id);
        tvState.TouchEncounter(id);
        return updated;
    }

    // ---- Boards (composable player-facing TV layouts) ----

    public Task<List<Board>> ListBoardsAsync() => boards.GetAllAsync();

    // There is deliberately no GET /boards/{id} endpoint; read straight from the store (the GetScreen idiom).
    public Task<Board?> GetBoardAsync(string id) => boards.GetAsync(id);

    // Mirrors PUT /boards/{id}: validate, upsert, diff the owned file set to drop images no longer referenced,
    // and refresh the TV if this board is the one being shown.
    public async Task<Board> UpsertBoardAsync(Board board, string id)
    {
        board.Id = id;
        BoardValidation.Validate(board);
        var oldFiles = (await boards.GetAsync(id))?.ReferencedFiles()
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        await boards.UpsertAsync(board);
        var newFiles = board.ReferencedFiles().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in oldFiles.Where(f => !newFiles.Contains(f)))
            images.Delete(stale);
        tvState.TouchBoard(id);
        return board;
    }

    // Mirrors DELETE /boards/{id}: release every owned file, then clear+scrub the display if it was showing.
    public async Task<bool> DeleteBoardAsync(string id)
    {
        var files = (await boards.GetAsync(id))?.ReferencedFiles().ToList();
        if (!await boards.DeleteAsync(id)) return false;
        foreach (var file in files ?? [])
            images.Delete(file);
        tvState.ForgetBoard(id);
        return true;
    }

    // ---- TV (the player-facing display) ----

    // Mirrors GET|POST /tv/show: push EXACTLY ONE of a stored image name / a board id / an encounter id (with an
    // optional label) to the display. Validates the target exists exactly like the endpoint; returns the new rev.
    public async Task<object> ShowOnTvAsync(string? image, string? board, string? encounter, string? label)
    {
        var imageName = image?.Trim();
        var boardId = board?.Trim();
        var encounterId = encounter?.Trim();
        var targets = (string.IsNullOrEmpty(imageName) ? 0 : 1)
                      + (string.IsNullOrEmpty(boardId) ? 0 : 1)
                      + (string.IsNullOrEmpty(encounterId) ? 0 : 1);
        if (targets != 1) // none, or more than one
            throw new ValidationException("error.tv.showTarget");

        var trimmedLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();

        if (!string.IsNullOrEmpty(imageName))
        {
            // The name is traversal-guarded inside FullPathForName and must exist on disk (like the endpoint).
            var full = images.FullPathForName(imageName);
            if (full is null || !File.Exists(full))
                throw new ValidationException("error.tv.imageNotFound", imageName);
            return new { rev = tvState.Show("image", imageName, trimmedLabel), image = imageName };
        }

        if (!string.IsNullOrEmpty(boardId))
        {
            var b = await boards.GetAsync(boardId)
                ?? throw new ValidationException("error.tv.boardNotFound", boardId);
            // Default the display label to the board's own name; store the canonical DB-cased id.
            return new { rev = tvState.Show("board", b.Id, trimmedLabel ?? b.Name), board = b.Id };
        }

        var enc = await encounters.GetAsync(encounterId!)
            ?? throw new ValidationException("error.tv.encounterNotFound", encounterId!);
        return new { rev = tvState.ShowEncounter(enc.Id, trimmedLabel ?? enc.Name), encounter = enc.Id };
    }

    // Mirrors GET|POST /tv/clear.
    public object ClearTv() => new { rev = tvState.Clear(), cleared = true };

    // Mirrors GET /tv/state, slimmed for the model: the live rev + what's currently pushed (all null if cleared).
    public TvStatusInfo GetTvState()
    {
        var (rev, content) = tvState.Snapshot();
        return new TvStatusInfo(rev, content?.Kind, content?.Ref, content?.Label);
    }

    // ---- Context / control ----

    public IReadOnlyList<LightInfo> ListLights() =>
        lights.GetAll().Select(l => new LightInfo(l.Key, l.Name, l.Provider)).ToList();

    // Sound context only (no waveform backfill, unlike /sounds/list — this never mutates the store).
    public async Task<IReadOnlyList<SoundInfo>> ListSoundsAsync()
    {
        var all = await sounds.GetAllAsync();
        return all.Select(s => new SoundInfo(s.Id, s.Name, s.Category, s.Volume, s.Loop, s.DurationMs)).ToList();
    }

    public Task<List<SpotifyPlaylist>> ListSpotifyPlaylistsAsync() => WithSpotifyAsync(s => s.GetPlaylistsAsync());

    public Task<List<SpotifyTrack>> SearchSpotifyTracksAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Provide a search term.");
        return WithSpotifyAsync(s => s.SearchTracksAsync(query));
    }

    // Mirrors GET|POST /lights/default — the panel's reset-lights button.
    public async Task ResetLightsAsync()
    {
        var def = settings.Current.DefaultLight
            ?? throw new ArgumentException("No default lighting is set — configure it on the Settings page first.");
        effects.StopAll();
        // ILightService is scoped (the provider is chosen per-scope from settings) — apply inside a fresh scope.
        using var scope = scopeFactory.CreateScope();
        var bulb = scope.ServiceProvider.GetRequiredService<ILightService>();
        await bulb.ApplyAsync(def);
    }

    // Mirrors GET /lights/status: the normalized live bulb state (on/off, mode, brightness, colour/
    // temperature) plus the raw Tuya/Hue payload for diagnostics.
    public async Task<LightStatus> GetLightsStatusAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var bulb = scope.ServiceProvider.GetRequiredService<ILightService>();
        return await bulb.GetStatusAsync();
    }

    // SpotifyClient is a typed HttpClient (scoped) — resolve one inside a fresh scope per call, never cache it.
    // Kept for the Spotify-only browser (playlists/search), which is not part of the source seam.
    private async Task<T> WithSpotifyAsync<T>(Func<SpotifyClient, Task<T>> op)
    {
        using var scope = scopeFactory.CreateScope();
        return await op(scope.ServiceProvider.GetRequiredService<SpotifyClient>());
    }

    // MusicRouter is scoped (it composes the scoped sources) — resolve one inside a fresh scope per call.
    private async Task<T> WithMusicAsync<T>(Func<MusicRouter, Task<T>> op)
    {
        using var scope = scopeFactory.CreateScope();
        return await op(scope.ServiceProvider.GetRequiredService<MusicRouter>());
    }

    // The endpoints' adjust guard, shared by every /adjust op (see PartyEndpoints): a counter label is required
    // and EXACTLY ONE of delta (bump) / value (set) must be given. Throws the same ValidationExceptions (400) the
    // HTTP routes do — ValidationException is an ArgumentException, so both AI surfaces surface it as a tool error.
    private static void AdjustGuard(string? counter, int? delta, int? value)
    {
        if (string.IsNullOrWhiteSpace(counter))
            throw new ValidationException("error.party.adjustCounter");
        if (delta.HasValue == value.HasValue) // both, or neither
            throw new ValidationException("error.party.adjustTarget");
    }

    // Mirror of PartyEndpoints.TouchIfPartyShownAsync: a party change is instantly visible on the TV IF the
    // currently-shown content renders a live roster — a board with a party/enemies element, or (for a hero /
    // table-counter change) a shown encounter whose hero panel + Fear strip resolve live from the party. Bumps
    // the rev so an open display re-fetches within one poll; otherwise no pointless re-render.
    private async Task TouchIfPartyShownAsync(bool heroChange)
    {
        if (tvState.Current is not { } current) return;
        if (current.Kind == "board")
        {
            var board = await boards.GetAsync(current.Ref);
            if (board is not null && board.Elements.Any(e => e.Kind is "party" or "enemies"))
                tvState.TouchBoard(board.Id);
        }
        else if (heroChange && current.Kind == "encounter")
        {
            tvState.TouchEncounter(current.Ref);
        }
    }

    // Activate a configured scene best-effort inside a fresh scope for the scoped SceneActivator (issue #122
    // encounter-run semantics, mirroring EncounterEndpoints): "skipped" (none set) / "notFound" (since deleted) /
    // "ok" / "partial" / "error:<code>". Never rethrows — running an encounter still shows on the TV even if the
    // ambiance failed.
    private async Task<string> RunSceneBestEffortAsync(string? sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId)) return "skipped";
        var scene = await scenes.GetAsync(sceneId);
        if (scene is null) return "notFound";
        try
        {
            using var scope = scopeFactory.CreateScope();
            var activator = scope.ServiceProvider.GetRequiredService<SceneActivator>();
            var result = await activator.ActivateAsync(scene);
            return result.FullySucceeded ? "ok" : "partial";
        }
        catch (Exception ex)
        {
            return "error:" + ErrorClassifier.DisplayCodeFor(ex);
        }
    }

    private async Task<string> RunEventBestEffortAsync(string? eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return "skipped";
        var evt = await events.GetAsync(eventId);
        if (evt is null) return "notFound";
        try
        {
            using var scope = scopeFactory.CreateScope();
            var activator = scope.ServiceProvider.GetRequiredService<EventActivator>();
            var result = await activator.TriggerAsync(evt);
            return result.FullySucceeded ? "ok" : "partial";
        }
        catch (Exception ex)
        {
            return "error:" + ErrorClassifier.DisplayCodeFor(ex);
        }
    }
}
