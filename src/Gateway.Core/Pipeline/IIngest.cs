using System.Threading.Channels;
using Gateway.Core.Models;

namespace Gateway.Core.Pipeline;

/// <summary>
/// Ingestion stage - receives raw data from adapters
/// </summary>
public interface IIngest
{
    /// <summary>
    /// Channel for raw data from adapters
    /// </summary>
    Channel<RawData> InputChannel { get; }

    /// <summary>
    /// Start processing
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop processing
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Raw data from adapters before normalization
/// </summary>
public sealed class RawData
{
    public required string AdapterId { get; init; }
    public required string SourceId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required object Payload { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

