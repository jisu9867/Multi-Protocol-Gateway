namespace Gateway.Core.Common;

/// <summary>
/// Result type for operations that can succeed or fail
/// </summary>
/// <typeparam name="T">Success value type</typeparam>
/// <typeparam name="TError">Error type</typeparam>
public sealed class Result<T, TError>
{
    private readonly T? _value;
    private readonly TError? _error;
    private readonly bool _isSuccess;

    private Result(T value)
    {
        _value = value;
        _isSuccess = true;
    }

    private Result(TError error)
    {
        _error = error;
        _isSuccess = false;
    }

    /// <summary>
    /// Create a successful result
    /// </summary>
    public static Result<T, TError> Success(T value) => new(value);

    /// <summary>
    /// Create a failed result
    /// </summary>
    public static Result<T, TError> Failure(TError error) => new(error);

    /// <summary>
    /// Check if the result is successful
    /// </summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>
    /// Check if the result is a failure
    /// </summary>
    public bool IsFailure => !_isSuccess;

    /// <summary>
    /// Get the success value (throws if failure)
    /// </summary>
    public T Value => _isSuccess ? _value! : throw new InvalidOperationException("Cannot access Value of a failed Result");

    /// <summary>
    /// Get the error value (throws if success)
    /// </summary>
    public TError Error => !_isSuccess ? _error! : throw new InvalidOperationException("Cannot access Error of a successful Result");

    /// <summary>
    /// Match the result and execute appropriate function
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<TError, TResult> onFailure)
        => _isSuccess ? onSuccess(_value!) : onFailure(_error!);
}

/// <summary>
/// Result type without value (for operations that only return success/failure)
/// </summary>
public sealed class Result
{
    private readonly string? _error;
    private readonly bool _isSuccess;

    private Result(bool isSuccess, string? error = null)
    {
        _isSuccess = isSuccess;
        _error = error;
    }

    /// <summary>
    /// Create a successful result
    /// </summary>
    public static Result Success() => new(true);

    /// <summary>
    /// Create a failed result
    /// </summary>
    public static Result Failure(string error) => new(false, error);

    /// <summary>
    /// Check if the result is successful
    /// </summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>
    /// Check if the result is a failure
    /// </summary>
    public bool IsFailure => !_isSuccess;

    /// <summary>
    /// Get the error message (throws if success)
    /// </summary>
    public string Error => !_isSuccess ? _error! : throw new InvalidOperationException("Cannot access Error of a successful Result");
}

/// <summary>
/// Simple error type with message and optional code
/// </summary>
public sealed class Error
{
    public required string Message { get; init; }
    public string? Code { get; init; }
    public Exception? Exception { get; init; }

    public static Error Create(string message, string? code = null, Exception? exception = null)
        => new() { Message = message, Code = code, Exception = exception };
}

