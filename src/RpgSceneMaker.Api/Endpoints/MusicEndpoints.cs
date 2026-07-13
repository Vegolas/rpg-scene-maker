using RpgSceneMaker.Api.Errors;
using RpgSceneMaker.Api.Services;

namespace RpgSceneMaker.Api.Endpoints;

public static class MusicEndpoints
{
    public static void MapMusicEndpoints(this WebApplication app)
    {
        var music = app.MapGroup("/music");

        // /music/play?id=spotify:playlist:… (or an open.spotify.com link)
        music.MapMethods("/play", EndpointHelpers.GetOrPost, async (string id, SpotifyClient spotify) =>
        {
            if (!SpotifyClient.IsSpotifyUri(id))
                throw new ValidationException("error.music.notSpotifyUri", id);
            await spotify.PlayAsync(id);
            return new { playing = id };
        });

        // A deliberate pause button should say why it did nothing, so surface a missing device here.
        music.MapMethods("/pause", EndpointHelpers.GetOrPost, async (SpotifyClient spotify) =>
        {
            await spotify.PauseAsync();
            return new { music = "paused" };
        });
        music.MapMethods("/resume", EndpointHelpers.GetOrPost, async (SpotifyClient spotify) => { await spotify.ResumeAsync(); return new { music = "playing" }; });
        music.MapMethods("/next", EndpointHelpers.GetOrPost, async (SpotifyClient spotify) => { await spotify.NextAsync(); return new { music = "next" }; });
        music.MapMethods("/previous", EndpointHelpers.GetOrPost, async (SpotifyClient spotify) => { await spotify.PreviousAsync(); return new { music = "previous" }; });

        // /music/volume?value=0.5   (0.0 - 1.0)
        music.MapMethods("/volume", EndpointHelpers.GetOrPost, async (double value, SpotifyClient spotify) =>
        {
            await spotify.SetVolumeAsync(value);
            return new { volume = value };
        });

        music.MapMethods("/shuffle", EndpointHelpers.GetOrPost, async (bool? value, SpotifyClient spotify) =>
        {
            await spotify.SetShuffleAsync(value ?? true);
            return new { shuffle = value ?? true };
        });

        // /music/repeat?mode=playlist   (off | track | playlist)
        music.MapMethods("/repeat", EndpointHelpers.GetOrPost, async (string mode, SpotifyClient spotify) =>
        {
            if (mode is not ("off" or "track" or "playlist"))
                throw new ValidationException("error.music.repeatMode");
            await spotify.SetRepeatAsync(mode);
            return new { repeat = mode };
        });

        // Spotify library / search / playback state.
        music.MapGet("/playlists", (SpotifyClient spotify) => spotify.GetPlaylistsAsync());
        music.MapGet("/search", (string q, SpotifyClient spotify) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                throw new ValidationException("error.music.searchTerm");
            return spotify.SearchTracksAsync(q);
        });
        music.MapGet("/state", (SpotifyClient spotify) => spotify.GetStateAsync());
    }
}
