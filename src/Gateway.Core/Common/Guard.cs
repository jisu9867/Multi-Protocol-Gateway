using System.Diagnostics;

namespace Gateway.Core.Common;

/// <summary>
/// Guard clauses for parameter validation
/// </summary>
public static class Guard
{
    /// <summary>
    /// Ensure value is not null
    /// </summary>
    [DebuggerStepThrough]
    public static T NotNull<T>(T? value, string paramName) where T : class
    {
        return value ?? throw new ArgumentNullException(paramName);
    }

    /// <summary>
    /// Ensure value is not null or empty (for strings)
    /// </summary>
    [DebuggerStepThrough]
    public static string NotNullOrEmpty(string? value, string paramName)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value cannot be null or empty", paramName);
        return value;
    }

    /// <summary>
    /// Ensure value is not null or whitespace (for strings)
    /// </summary>
    [DebuggerStepThrough]
    public static string NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace", paramName);
        return value;
    }

    /// <summary>
    /// Ensure condition is true
    /// </summary>
    [DebuggerStepThrough]
    public static void Requires(bool condition, string message)
    {
        if (!condition)
            throw new ArgumentException(message);
    }

    /// <summary>
    /// Ensure value is in valid range
    /// </summary>
    [DebuggerStepThrough]
    public static T InRange<T>(T value, T min, T max, string paramName) where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
            throw new ArgumentOutOfRangeException(paramName, value, $"Value must be between {min} and {max}");
        return value;
    }

    /// <summary>
    /// Ensure value is greater than threshold
    /// </summary>
    [DebuggerStepThrough]
    public static T GreaterThan<T>(T value, T threshold, string paramName) where T : IComparable<T>
    {
        if (value.CompareTo(threshold) <= 0)
            throw new ArgumentOutOfRangeException(paramName, value, $"Value must be greater than {threshold}");
        return value;
    }
}

