using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Services.Ai;

namespace RpgSceneMaker.Api.Endpoints;

public static class AssistantEndpoints
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        // Nothing is mapped at the bare "/assistant" path: the Blazor panel's Assistant tab lives there, so a
        // full-page load of /assistant must fall through to index.html (same reason /events uses /events/list).
        var assistant = app.MapGroup("/assistant");

        // Queue a message and start a run. 202 when accepted; 409 when a run is already in progress. A blank
        // message (ArgumentException → 400) or an unset key (InvalidOperationException → 503 "Not configured")
        // are mapped by the Program.cs error middleware.
        assistant.MapPost("/send", (AssistantSendInput input, AssistantService svc) =>
            svc.TrySend(input.Text)
                ? Results.Accepted(value: new { accepted = true })
                : Results.Conflict(new { error = "Assistant is busy — wait or stop the current run." }));

        // The panel polls this (~1 s); pass the last seen rev to get entries only when something changed.
        // rev defaults to 0 so a bare /assistant/state returns the full state (svc comes first so rev can
        // carry a default — required minimal-API parameters must precede optional ones).
        assistant.MapGet("/state", (AssistantService svc, long rev = 0) =>
            AssistantStateDto.From(svc.GetState(rev)));

        // Command endpoints → GET and POST (the project convention).
        assistant.MapMethods("/stop", EndpointHelpers.GetOrPost, (AssistantService svc) =>
            new { stopped = svc.Stop() });

        assistant.MapMethods("/clear", EndpointHelpers.GetOrPost, (AssistantService svc) =>
            svc.Clear()
                ? Results.Ok(new { cleared = true })
                : Results.Conflict(new { error = "Assistant is busy — stop the current run before clearing." }));
    }
}
