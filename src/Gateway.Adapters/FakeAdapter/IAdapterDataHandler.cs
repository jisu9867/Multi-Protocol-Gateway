namespace Gateway.Adapters.FakeAdapter;

/// <summary>
/// Handler interface for adapter data
/// </summary>
public interface IAdapterDataHandler
{
    /// <summary>
    /// Handle data from adapter
    /// </summary>
    Task HandleDataAsync(
        string adapterId,
        string sourceId,
        DateTime timestamp,
        Dictionary<string, object> payload,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);
}

