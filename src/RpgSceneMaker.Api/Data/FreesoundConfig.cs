using System.ComponentModel.DataAnnotations.Schema;

namespace RpgSceneMaker.Api.Data;

/// <summary>
/// Freesound.org connection state (bring-your-own-key), stored as a single row (Id = 1) in SQLite. Token-only
/// auth: <see cref="ApiKey"/> is a Freesound API token used to search and download HQ MP3 previews server-side.
/// Mirrors <see cref="AssistantConfig"/> — the key is write-only from the panel's point of view: it is stored
/// here and sent to Freesound, but never returned by any endpoint once saved.
/// </summary>
public class FreesoundConfig
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Freesound API token from freesound.org/apiv2/apply. Empty until the user configures it.</summary>
    public string ApiKey { get; set; } = "";

    [NotMapped]
    public bool IsConfigured => ApiKey != "";
}
