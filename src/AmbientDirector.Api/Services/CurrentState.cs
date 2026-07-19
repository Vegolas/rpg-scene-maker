namespace AmbientDirector.Api.Services;

/// <summary>Remembers the last activated scene so the UI can highlight it.</summary>
public class CurrentState
{
    public string? ActiveSceneId { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
