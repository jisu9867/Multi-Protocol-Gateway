namespace Gateway.Core.Pipeline;

/// <summary>
/// Pipeline metrics interface
/// </summary>
public interface IPipelineMetrics
{
    /// <summary>
    /// Record ingested count
    /// </summary>
    void RecordIngested();

    /// <summary>
    /// Record normalized count
    /// </summary>
    void RecordNormalized();

    /// <summary>
    /// Record routed count
    /// </summary>
    void RecordRouted();

    /// <summary>
    /// Record persisted count
    /// </summary>
    void RecordPersisted();

    /// <summary>
    /// Record dropped count
    /// </summary>
    void RecordDropped();

    /// <summary>
    /// Record processing latency
    /// </summary>
    void RecordLatency(TimeSpan latency);

    /// <summary>
    /// Get current metrics snapshot
    /// </summary>
    PipelineMetricsSnapshot GetSnapshot();

    /// <summary>
    /// Get queue length for a stage
    /// </summary>
    int GetQueueLength(string stageName);
}

/// <summary>
/// Pipeline metrics snapshot
/// </summary>
public sealed class PipelineMetricsSnapshot
{
    public long IngestedCount { get; init; }
    public long NormalizedCount { get; init; }
    public long RoutedCount { get; init; }
    public long PersistedCount { get; init; }
    public long DroppedCount { get; init; }
    public TimeSpan AverageLatency { get; init; }
    public Dictionary<string, int> QueueLengths { get; init; } = new();
}

