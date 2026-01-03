namespace Gateway.Core.Adapters;

/// <summary>
/// Protocol adapter interface for data ingestion
/// </summary>
public interface IAdapter
{
    /// <summary>
    /// Unique identifier for this adapter instance
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Adapter status
    /// </summary>
    AdapterStatus Status { get; }

    /// <summary>
    /// Start the adapter
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the adapter
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get health status
    /// </summary>
    Task<AdapterHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapter status enumeration
/// </summary>
public enum AdapterStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

/// <summary>
/// Adapter health information
/// </summary>
public sealed class AdapterHealth
{
    public required AdapterStatus Status { get; init; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}

