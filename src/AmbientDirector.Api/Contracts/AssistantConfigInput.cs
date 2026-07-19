namespace AmbientDirector.Api.Contracts;

// Body of PUT /setup/assistant/config (all optional — an empty ApiKey keeps the saved key so the model or
// provider can change without re-pasting it; the key is never echoed back).
public record AssistantConfigInput(string? Provider, string? ApiKey, string? Model);
