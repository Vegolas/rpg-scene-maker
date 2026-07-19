using System.Text.Json;

namespace AmbientDirector.Api.Services.Ai;

/// <summary>
/// JSON options for (de)serializing AI-tool entity payloads (scenes, events, light FX). Uses the web
/// defaults (camelCase, case-insensitive) so a tool's entity JSON is byte-identical to what the HTTP
/// endpoints accept and return — the models bind the same either way.
/// </summary>
public static class AiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
