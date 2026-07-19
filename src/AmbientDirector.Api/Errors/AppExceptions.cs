namespace AmbientDirector.Api.Errors;

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

/// <summary>
/// A "no such entity" failure — a referenced id doesn't exist — mapping to HTTP 404 via its own arm in
/// <see cref="ErrorClassifier"/>, carrying a localizable error <see cref="Code"/> and its <see cref="Args"/>.
/// Generic like <see cref="ValidationException"/>: the specific entity lives in the <see cref="Code"/>.
/// </summary>
public sealed class NotFoundException(string code, params object?[] args) : Exception, IErrorCode
{
    public string Code => code;
    public IReadOnlyList<object?> Args => args;
    public override string Message => ErrorMessages.English(code, args);
}

/// <summary>
/// A "resource is busy, retry later" conflict — mapping to HTTP 409 via its own arm in
/// <see cref="ErrorClassifier"/>, carrying a localizable error <see cref="Code"/> and its <see cref="Args"/>.
/// </summary>
public sealed class ConflictException(string code, params object?[] args) : Exception, IErrorCode
{
    public string Code => code;
    public IReadOnlyList<object?> Args => args;
    public override string Message => ErrorMessages.English(code, args);
}
