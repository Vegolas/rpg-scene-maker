namespace RpgSceneMaker.Api.Endpoints;

internal static class EndpointHelpers
{
    // Every command endpoint accepts GET and POST so the Stream Deck "System → Website" action works
    // without a plugin. Keep this when adding command routes.
    public static readonly string[] GetOrPost = ["GET", "POST"];
}
