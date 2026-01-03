using System.Threading.Channels;
using Gateway.Core.Models;

namespace Gateway.Core.Pipeline;

/// <summary>
/// Sink stage - persists events (database, file, etc.)
/// </summary>
public interface ISink
{
    /// <summary>
    /// Input channel for events to persist
    /// </summary>
    Channel<TelemetryEvent> InputChannel { get; }

    /// <summary>
    /// Start processing
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop processing
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

