using RpgSceneMaker.Api.Logging;

namespace RpgSceneMaker.Api.Endpoints;

public static class LogEndpoints
{
    public static void MapLogEndpoints(this WebApplication app)
    {
        var logs = app.MapGroup("/logs");

        // Sub-route only (not exact "/logs"), so a full-page load of the panel's /logs route falls through
        // to the Blazor app instead of this endpoint — same pattern as /music and /lights.
        logs.MapGet("/list", (InMemoryLogStore store) => store.Snapshot());

        logs.MapMethods("/clear", EndpointHelpers.GetOrPost, (InMemoryLogStore store) =>
        {
            store.Clear();
            return new { cleared = true };
        });
    }
}
