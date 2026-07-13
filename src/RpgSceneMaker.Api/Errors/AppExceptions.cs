namespace RpgSceneMaker.Api.Errors;

/// <summary>
/// A validation failure — maps to HTTP 400 via the <see cref="ArgumentException"/> arm of the error
/// middleware (so existing <c>Assert.ThrowsAny&lt;ArgumentException&gt;</c> tests still hold) — carrying a
/// localizable error <see cref="Code"/> and its interpolation <see cref="Args"/>. <see cref="Message"/>
/// renders lazily to English (for logs / fallback) from the embedded <c>en.json</c>.
/// </summary>
public sealed class ValidationException(string code, params object?[] args) : ArgumentException, IErrorCode
{
    public string Code => code;
    public IReadOnlyList<object?> Args => args;
    public override string Message => ErrorMessages.English(code, args);
}

/// <summary>
/// A "not configured yet" failure — maps to HTTP 503 via the <see cref="InvalidOperationException"/> arm —
/// carrying a localizable error <see cref="Code"/> and its <see cref="Args"/>.
/// </summary>
public sealed class NotConfiguredException(string code, params object?[] args) : InvalidOperationException, IErrorCode
{
    public string Code => code;
    public IReadOnlyList<object?> Args => args;
    public override string Message => ErrorMessages.English(code, args);
}
