using System.ComponentModel.DataAnnotations.Schema;

namespace AmbientDirector.Api.Data;

/// <summary>
/// In-panel assistant (bring-your-own-key) settings, stored as a single row (Id = 1) in SQLite. One active
/// provider at a time: <see cref="Provider"/> selects the backend ("anthropic" | "openai" | "gemini") and
/// <see cref="ApiKey"/> / <see cref="Model"/> are that provider's key and model id. The API key is never
/// returned by any endpoint once saved.
/// </summary>
public class AssistantConfig
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Active AI backend: "anthropic" (default), "openai" or "gemini".</summary>
    public string Provider { get; set; } = "anthropic";

    /// <summary>API key for the active <see cref="Provider"/>. Empty until the user configures it.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Model id the assistant talks to for the active provider (e.g. claude-opus-4-8, gpt-4o, gemini-2.0-flash).</summary>
    public string Model { get; set; } = "claude-opus-4-8";

    [NotMapped]
    public bool IsConfigured => ApiKey != "";
}
