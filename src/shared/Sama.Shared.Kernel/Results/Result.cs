namespace Sama.Shared.Kernel.Results;

/// <summary>
/// Outcome of an operation that can fail for an expected, domain-meaningful reason.
/// Exceptions stay reserved for genuinely exceptional conditions (a dropped connection,
/// a bug); anything a caller might reasonably need to handle comes back as a Result.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("A failed result must carry an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>A <see cref="Result"/> that carries a value when successful.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
        => _value = value;

    /// <summary>The value. Throws when the result is a failure — check <see cref="Result.IsSuccess"/> first.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error.Code}).");

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);

    /// <summary>Collapses both branches into a single value, so callers cannot forget the failure path.</summary>
    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<Error, TResult> onFailure)
        => IsSuccess ? onSuccess(_value!) : onFailure(Error);

    /// <summary>Transforms the value, leaving a failure untouched.</summary>
    public Result<TNext> Map<TNext>(Func<TValue, TNext> transform)
        => IsSuccess ? Success(transform(_value!)) : Failure<TNext>(Error);
}
