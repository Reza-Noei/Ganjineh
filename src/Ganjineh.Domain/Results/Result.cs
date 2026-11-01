namespace Ganjineh.Domain;

/// <summary>Result without a payload. Exceptions never cross the Application boundary.</summary>
public sealed class Result
{
    private Result(bool isSuccess, AppError? error)
    {
        IsSuccess = isSuccess;
        Error = error!;
    }

    public bool IsSuccess { get; }
    public AppError Error { get; }

    public static Result Ok() => new(true, null);

    public static Result Fail(AppError error) => new(false, error ?? throw new ArgumentNullException(nameof(error)));
}

/// <summary>Result with a payload; <see cref="Value"/> is only readable on success.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
        Error = null!;
    }

    private Result(AppError error)
    {
        _value = default;
        IsSuccess = false;
        Error = error;
    }

    public bool IsSuccess { get; }
    public AppError Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Result does not carry a value; check IsSuccess first.");

    public static Result<T> Ok(T value) => new(value);

    public static Result<T> Fail(AppError error) => new(error ?? throw new ArgumentNullException(nameof(error)));
}
