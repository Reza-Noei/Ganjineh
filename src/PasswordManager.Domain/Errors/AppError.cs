namespace PasswordManager.Domain;

/// <summary>
/// Stable error taxonomy shared by every application service (architecture.md §3).
/// Errors carry localization keys — never exception details containing secret material.
/// </summary>
public enum ErrorCode
{
    AuthenticationFailed,
    FormatError,
    StorageError,
    PolicyViolation,
    Cancelled,
    Unexpected,
}

/// <summary>A typed application error; <see cref="MessageKey"/> resolves through the localizer.</summary>
public sealed record AppError(ErrorCode Code, string MessageKey, Exception? Inner = null)
{
    /// <summary>Deliberately indistinguishable between "wrong credential" and "tampering".</summary>
    public static AppError AuthenticationFailed() => new(ErrorCode.AuthenticationFailed, "Error.AuthenticationFailed");

    public static AppError Format(string messageKey = "Error.Format", Exception? inner = null) => new(ErrorCode.FormatError, messageKey, inner);

    public static AppError Storage(string messageKey = "Error.Storage", Exception? inner = null) => new(ErrorCode.StorageError, messageKey, inner);

    public static AppError Policy(string messageKey) => new(ErrorCode.PolicyViolation, messageKey);

    public static AppError Cancelled() => new(ErrorCode.Cancelled, "Error.Cancelled");

    public static AppError Unexpected(string messageKey = "Error.Unexpected", Exception? inner = null) => new(ErrorCode.Unexpected, messageKey, inner);
}
