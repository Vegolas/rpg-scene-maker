namespace AmbientDirector.Ui.Contracts;

// Mirrors the API's event shapes (contracts are duplicated per project by design — keep in sync by hand).
// `Image` is an optional full-art tile background. `Timeline` is the advanced editor's shape;
// `Flash`/`SoundEffects` are the legacy shape (still read for import, always written back as null/[]
// once an event is saved from the timeline editor).
public record EventDto(string Id, string Name, EventFlashDto? Flash, List<string>? SoundEffects, string? Image = null, EventTimelineDto? Timeline = null, EventAfterDto? After = null);
public record EventFlashDto(string Color, int Brightness, int DurationMs);
// What the lights do when the event finishes. Mode: "previous" (restore prior lighting — the default),
// "scene" (fully activate SceneId — lights + music) or "default" (apply the configured default light).
public record EventAfterDto(string Mode, string? SceneId);
public record EventTriggerDto(string Event, string Light, string Sound, bool FullySucceeded);

// Timeline wire shape: overlapping-free lists of sound + light clips along a shared time axis (ms).
public record EventTimelineDto(List<TimelineSoundDto> Sounds, List<TimelineLightDto> Lights);
// DurationMs null = play to the file's natural end; Volume null = the sound's own configured volume.
public record TimelineSoundDto(string SoundId, int StartMs, int? DurationMs, double? Volume);
// LightKey null/"" = all lights. Effect shape mirrors the scene editor's LightEffect (EffectDto).
public record TimelineLightDto(string? LightKey, int StartMs, int DurationMs,
    bool? Power, string? Color, int? Brightness, int? Temperature, EffectDto? Effect);

// GET /events/state — id of the timeline event currently playing, or null.
public record EventStateDto(string? RunningId);
// POST /events/stop
public record EventStopDto(bool Stopped);

// ---------- mutable form models (bound in the editor; convert to wire DTOs on save) ----------

public class EventEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public TimelineEdit Timeline { get; set; } = new();
    public string? Image { get; set; }
    public EventAfterEdit After { get; set; } = new();

    // Always writes the timeline and clears the legacy fields (flash:null, soundEffects:[]). A plain
    // "previous" ending is sent as null, so an ordinary event's wire shape is unchanged.
    public EventDto ToDto() => new(Id, Name, null, [], Image, Timeline.ToDto(),
        After.Mode == "previous" ? null : After.ToDto());

    // Build an editable model from an event, converting a legacy (flash/soundEffects) event into
    // equivalent timeline clips: the flash → one all-lights light clip at t=0; each legacy sound id →
    // one sound clip at t=0. A looping sound becomes open-ended (DurationMs null → plays until the event
    // is stopped, as it did before); a one-shot keeps its natural length, clamped to the server minimum
    // (a natural length under that, or an unknown one, would otherwise make the server 400 the clip).
    public static EventEdit FromDto(EventDto evt, IReadOnlyList<SoundDto> sounds)
    {
        var edit = new EventEdit { Id = evt.Id, Name = evt.Name, Image = evt.Image, After = EventAfterEdit.FromDto(evt.After) };

        if (evt.Timeline is { } tl)
        {
            edit.Timeline = TimelineEdit.FromDto(tl);
            return edit;
        }

        // Legacy → timeline conversion.
        if (evt.Flash is { } flash)
        {
            edit.Timeline.Lights.Add(new TimelineLightEdit
            {
                LightKey = null,
                StartMs = 0,
                DurationMs = Math.Max(TimelineEdit.MinClipMs, flash.DurationMs),
                Mode = "color",
                Color = flash.Color,
                Brightness = flash.Brightness,
            });
        }
        foreach (var sid in evt.SoundEffects ?? [])
        {
            var sound = sounds.FirstOrDefault(s => string.Equals(s.Id, sid, StringComparison.OrdinalIgnoreCase));
            int? duration = sound?.Loop == true
                ? null                                                          // loop until the event is stopped
                : Math.Max(TimelineEdit.MinClipMs, sound?.NaturalMs ?? TimelineEdit.MinClipMs);
            edit.Timeline.Sounds.Add(new TimelineSoundEdit { SoundId = sid, StartMs = 0, DurationMs = duration });
        }
        return edit;
    }
}

// Mutable form model for the event's ending. Mode: "previous" | "scene" | "default".
public class EventAfterEdit
{
    public string Mode { get; set; } = "previous";
    public string? SceneId { get; set; }

    // SceneId only travels when the ending is a scene, so switching away doesn't persist a stale target.
    public EventAfterDto ToDto() => new(Mode, Mode == "scene" ? SceneId : null);

    public static EventAfterEdit FromDto(EventAfterDto? d) => d is null
        ? new EventAfterEdit()
        : new EventAfterEdit { Mode = string.IsNullOrWhiteSpace(d.Mode) ? "previous" : d.Mode, SceneId = d.SceneId };
}

public class TimelineEdit
{
    // Minimum duration the server accepts for ANY clip (sound or light); a shorter clip 400s.
    public const int MinClipMs = 100;
    public const int MaxEndMs = 600_000; // 10 minutes

    public List<TimelineSoundEdit> Sounds { get; set; } = [];
    public List<TimelineLightEdit> Lights { get; set; } = [];

    public bool IsEmpty => Sounds.Count == 0 && Lights.Count == 0;

    // Latest end-of-clip across both tracks, in ms.
    public int EndMs => Math.Max(
        Sounds.Count == 0 ? 0 : Sounds.Max(s => s.StartMs + (s.DurationMs ?? 0)),
        Lights.Count == 0 ? 0 : Lights.Max(l => l.StartMs + l.DurationMs));

    public EventTimelineDto ToDto() => new(
        Sounds.Select(s => s.ToDto()).ToList(),
        Lights.Select(l => l.ToDto()).ToList());

    public static TimelineEdit FromDto(EventTimelineDto tl) => new()
    {
        Sounds = (tl.Sounds ?? []).Select(TimelineSoundEdit.FromDto).ToList(),
        Lights = (tl.Lights ?? []).Select(TimelineLightEdit.FromDto).ToList(),
    };
}

public class TimelineSoundEdit
{
    // Transient per-session id so the timeline UI / JS drag interop can address this clip; not persisted.
    public string Uid { get; } = Guid.NewGuid().ToString("N");
    public string SoundId { get; set; } = "";
    public int StartMs { get; set; }
    // null = play to the file's natural end.
    public int? DurationMs { get; set; }
    // null = use the sound's own configured volume.
    public double? Volume { get; set; }

    public TimelineSoundDto ToDto() => new(SoundId, StartMs, DurationMs, Volume);

    public static TimelineSoundEdit FromDto(TimelineSoundDto d) => new()
    {
        SoundId = d.SoundId,
        StartMs = d.StartMs,
        DurationMs = d.DurationMs,
        Volume = d.Volume,
    };
}

public class TimelineLightEdit
{
    // Transient per-session id so the timeline UI / JS drag interop can address this clip; not persisted.
    public string Uid { get; } = Guid.NewGuid().ToString("N");
    // null/"" = all lights; otherwise a key from /lights/list.
    public string? LightKey { get; set; }
    public int StartMs { get; set; }
    public int DurationMs { get; set; } = 1000;

    // "color" | "white" | "off" — the wire shape derives power/color/temperature from this.
    public string Mode { get; set; } = "color";
    public string Color { get; set; } = "#ffffff";
    public int Brightness { get; set; } = 100;
    public int Temperature { get; set; } = 40;
    public EffectEdit Effect { get; set; } = new();

    public TimelineLightDto ToDto()
    {
        var key = string.IsNullOrWhiteSpace(LightKey) ? null : LightKey;
        return Mode switch
        {
            "white" => new TimelineLightDto(key, StartMs, DurationMs, true, null, Brightness, Temperature, Effect.ToDto()),
            "off" => new TimelineLightDto(key, StartMs, DurationMs, false, null, null, null, null),
            _ => new TimelineLightDto(key, StartMs, DurationMs, true, Color, Brightness, null, Effect.ToDto()),
        };
    }

    public static TimelineLightEdit FromDto(TimelineLightDto d)
    {
        var edit = new TimelineLightEdit
        {
            LightKey = d.LightKey,
            StartMs = d.StartMs,
            DurationMs = Math.Max(TimelineEdit.MinClipMs, d.DurationMs),
            Brightness = d.Brightness ?? 100,
            Temperature = d.Temperature ?? 40,
        };
        if (d.Color is { } color) { edit.Mode = "color"; edit.Color = color; }
        else if (d.Power == false) { edit.Mode = "off"; }
        else { edit.Mode = "white"; }

        edit.Effect = EffectEdit.FromDto(d.Effect);
        return edit;
    }
}
