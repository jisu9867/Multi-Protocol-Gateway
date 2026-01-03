namespace Gateway.Core.Pipeline;

/// <summary>
/// Default in-memory implementation of pipeline metrics
/// </summary>
public sealed class DefaultPipelineMetrics : IPipelineMetrics
{
    private long _ingestedCount;
    private long _normalizedCount;
    private long _routedCount;
    private long _persistedCount;
    private long _droppedCount;
    private readonly List<TimeSpan> _latencies = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _queueLengths = new();

    public void RecordIngested()
    {
        Interlocked.Increment(ref _ingestedCount);
    }

    public void RecordNormalized()
    {
        Interlocked.Increment(ref _normalizedCount);
    }

    public void RecordRouted()
    {
        Interlocked.Increment(ref _routedCount);
    }

    public void RecordPersisted()
    {
        Interlocked.Increment(ref _persistedCount);
    }

    public void RecordDropped()
    {
        Interlocked.Increment(ref _droppedCount);
    }

    public void RecordLatency(TimeSpan latency)
    {
        lock (_lock)
        {
            _latencies.Add(latency);
            // Keep only last 1000 latencies
            if (_latencies.Count > 1000)
            {
                _latencies.RemoveAt(0);
            }
        }
    }

    public PipelineMetricsSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var avgLatency = _latencies.Count > 0
                ? TimeSpan.FromMilliseconds(_latencies.Average(l => l.TotalMilliseconds))
                : TimeSpan.Zero;

            return new PipelineMetricsSnapshot
            {
                IngestedCount = Interlocked.Read(ref _ingestedCount),
                NormalizedCount = Interlocked.Read(ref _normalizedCount),
                RoutedCount = Interlocked.Read(ref _routedCount),
                PersistedCount = Interlocked.Read(ref _persistedCount),
                DroppedCount = Interlocked.Read(ref _droppedCount),
                AverageLatency = avgLatency,
                QueueLengths = new Dictionary<string, int>(_queueLengths)
            };
        }
    }

    public int GetQueueLength(string stageName)
    {
        lock (_lock)
        {
            return _queueLengths.TryGetValue(stageName, out var length) ? length : 0;
        }
    }

    public void UpdateQueueLength(string stageName, int length)
    {
        lock (_lock)
        {
            _queueLengths[stageName] = length;
        }
    }
}

