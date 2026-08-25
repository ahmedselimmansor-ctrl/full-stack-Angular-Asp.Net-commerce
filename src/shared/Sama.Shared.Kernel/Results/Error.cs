namespace Sama.Shared.Kernel.Results;

/// <summary>
/// Classifies a failure so transport layers can map it without string-matching.
/// The HTTP mapping lives in Sama.Shared.Infrastructure's exception/result middleware.
/// </summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    TooManyRequests,
    Unavailable,
    Unexpected,
}

/// <summary>
/// A failure with a stable machine-readable <see cref="Code"/>. Clients branch on the code;
/// <see cref="Message"/> is for humans and may change without notice.
/// </summary>
public sealed record Error
{
    public const string SeparatorlessCodePattern = "area.subject.reason";

    private Error(string code, string message, ErrorType type, IReadOnlyDictionary<string, string[]>? details)
    {
        Code = code;
        Message = message;
        Type = type;
        Details = details;
    }

    public string Code { get; }

    public string Message { get; }

    public ErrorType Type { get; }

    /// <summary>Per-field messages, populated for <see cref="ErrorType.Validation"/>.</summary>
    public IReadOnlyDictionary<string, string[]>? Details { get; }

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? details = null)
        => new(code, message, ErrorType.Validation, details);

    public static Error NotFound(string code, string message)
        => new(code, message, ErrorType.NotFound, null);

    public static Error Conflict(string code, string message)
        => new(code, message, ErrorType.Conflict, null);

    public static Error Forbidden(string code, string message)
        => new(code, message, ErrorType.Forbidden, null);

    public static Error Unauthorized(string code, string message)
        => new(code, message, ErrorType.Unauthorized, null);

    public static Error TooManyRequests(string code, string message)
        => new(code, message, ErrorType.TooManyRequests, null);

    public static Error Unavailable(string code, string message)
        => new(code, message, ErrorType.Unavailable, null);

    public static Error Unexpected(string code, string message)
        => new(code, message, ErrorType.Unexpected, null);

    /// <summary>Sentinel carried by successful results. Never surfaced to a caller.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Unexpected, null);

    public override string ToString() => $"{Code}: {Message}";
}
