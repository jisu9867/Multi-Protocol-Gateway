using System.Collections.Generic;
using System.Threading.Channels;
using Gateway.Core.Models;

namespace Gateway.Core.Pipeline;

/// <summary>
/// Event ingestion interface for receiving events from adapters
/// 
/// Design Decision: IAsyncEnumerable approach chosen over ChannelWriter for the following reasons:
/// 1. More natural async/await pattern - fits well with C# async streams
/// 2. Better composability - can use LINQ and other async enumerable operations
/// 3. Backpressure handled by consumer - consumer controls the pace of enumeration
/// 4. Simpler interface - no need to manage ChannelWriter lifecycle
/// 5. Better testability - easier to create test data with async enumerables
/// 
/// However, ChannelWriter approach could be better if:
/// - Multiple producers need to write to the same channel
/// - More explicit backpressure control is needed via bounded channels
/// - Integration with existing Channel-based pipeline components
/// 
/// For this implementation, IAsyncEnumerable is chosen for cleaner API design.
/// </summary>
public interface IEventIngestor
{
    /// <summary>
    /// Asynchronously enumerate events from adapters
    /// Consumer controls backpressure by controlling enumeration pace
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of telemetry events</returns>
    IAsyncEnumerable<TelemetryEvent> IngestAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Alternative ChannelWriter-based ingestion interface
/// Use this if multiple producers or explicit channel control is needed
/// </summary>
public interface IEventIngestorChannel
{
    /// <summary>
    /// Channel writer for adapters to push events
    /// </summary>
    ChannelWriter<TelemetryEvent> EventChannel { get; }

    /// <summary>
    /// Start processing events from the channel
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop processing events
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

