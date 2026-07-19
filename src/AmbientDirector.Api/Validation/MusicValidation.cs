using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Validation;

/// <summary>Guards local music-library metadata before it reaches the store; failures map to HTTP 400.</summary>
public static class MusicValidation
{
    private const int MaxNameLength = 120;
    private const int MaxArtistLength = 120;

    public static void ValidateTrack(MusicTrack track)
    {
        if (string.IsNullOrWhiteSpace(track.Name))
            throw new ValidationException("error.common.nameRequired");
        if (track.Name.Length > MaxNameLength)
            throw new ValidationException("error.musicTrack.nameLength", MaxNameLength);
        if (track.Artist.Length > MaxArtistLength)
            throw new ValidationException("error.musicTrack.artistLength", MaxArtistLength);
    }

    public static void ValidatePlaylist(MusicPlaylist playlist)
    {
        if (string.IsNullOrWhiteSpace(playlist.Id))
            throw new ValidationException("error.common.idRequired");
        if (!playlist.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ValidationException("error.common.idSlug");
        if (string.IsNullOrWhiteSpace(playlist.Name))
            throw new ValidationException("error.common.nameRequired");
        if (playlist.Name.Length > MaxNameLength)
            throw new ValidationException("error.musicPlaylist.nameLength", MaxNameLength);
        playlist.TrackIds ??= [];
    }
}
