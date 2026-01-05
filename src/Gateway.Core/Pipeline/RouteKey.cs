namespace Gateway.Core.Pipeline;

/// <summary>
/// Route key enumeration for event routing
/// </summary>
public enum RouteKey
{
    /// <summary>
    /// Default route
    /// </summary>
    Default = 0,

    /// <summary>
    /// High priority route
    /// </summary>
    HighPriority = 1,

    /// <summary>
    /// Low priority route
    /// </summary>
    LowPriority = 2,

    /// <summary>
    /// Real-time route
    /// </summary>
    RealTime = 3,

    /// <summary>
    /// Batch route
    /// </summary>
    Batch = 4,

    /// <summary>
    /// Archive route
    /// </summary>
    Archive = 5
}

