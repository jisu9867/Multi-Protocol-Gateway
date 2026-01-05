namespace Gateway.Core.Common;

/// <summary>
/// TimeProvider utilities and helpers
/// Note: TimeProvider is available in .NET 8+ as a built-in type
/// This file provides documentation and common usage patterns
/// </summary>
public static class TimeProviderExtensions
{
    // TimeProvider is built-in to .NET 8+, so extensions are typically not needed
    // However, this file documents common usage patterns
    
    /// <summary>
    /// System.TimeProvider provides:
    /// - GetUtcNow() - returns DateTimeOffset
    /// - GetLocalNow() - returns DateTimeOffset
    /// - CreateTimer() - for creating timers
    /// 
    /// Usage example:
    /// var timeProvider = TimeProvider.System;
    /// var now = timeProvider.GetUtcNow();
    /// </summary>
}

