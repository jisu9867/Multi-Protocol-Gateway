namespace Gateway.Core.Adapters;

/// <summary>
/// Protocol adapter interface for data ingestion
/// Adapters push raw events into the pipeline
/// </summary>
public interface IProtocolAdapter
{
    /// <summary>
    /// Adapter name/type identifier
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Start the adapter
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the adapter
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current adapter status
    /// </summary>
    AdapterStatus GetStatus();
}

