namespace RpgSceneMaker.Api.Contracts;

// Body of PUT /setup/freesound/config. The token is write-only (never echoed back); a blank value is rejected.
public record FreesoundConfigInput(string? ApiKey);
