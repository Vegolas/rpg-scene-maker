using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Services.Ai;

namespace AmbientDirector.Api.Endpoints;

public static class AssistantEndpoints
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        // Nothing is mapped at the bare "/assistant" path: the Blazor panel's Assistant tab lives there, so a
        // full-page load of /assistant must fall through to index.html (same reason /events uses /events/list).
        var assistant = app.MapGroup("/assistant");

        // Queue a message and start a run. 202 when accepted; a run already in progress throws
        // ConflictException → 409. A blank message (ArgumentException → 400) or an unset key
        // (InvalidOperationException → 503 "Not configured") are mapped by the Program.cs error middleware too.
        assistant.MapPost("/send", (AssistantSendInput input, AssistantService svc) =>
        {
            if (!svc.TrySend(input.Text))
                throw new ConflictException("error.assistant.busySend");
            return Results.Accepted(value: new { accepted = true });
        });

        // The panel polls this (~1 s); pass the last seen rev to get entries only when something changed.
        // rev defaults to 0 so a bare /assistant/state returns the full state (svc comes first so rev can
        // carry a default — required minimal-API parameters must precede optional ones).
        assistant.MapGet("/state", (AssistantService svc, long rev = 0) =>
            AssistantStateDto.From(svc.GetState(rev)));

        // Command endpoints → GET and POST (the project convention).
        assistant.MapMethods("/stop", EndpointHelpers.GetOrPost, (AssistantService svc) =>
            new { stopped = svc.Stop() });

        assistant.MapMethods("/clear", EndpointHelpers.GetOrPost, (AssistantService svc) =>
        {
            if (!svc.Clear())
                throw new ConflictException("error.assistant.busyClear");
            return Results.Ok(new { cleared = true });
        });
    }
}
