using System.Threading.Channels;
using Gateway.Core.Models;

namespace Gateway.Core.Pipeline;

/// <summary>
/// Normalization stage - converts raw data to TelemetryEvent
/// </summary>
public interface INormalize
{
    /// <summary>
    /// Input channel for raw data
    /// </summary>
    Channel<RawData> InputChannel { get; }

    /// <summary>
    /// Output channel for normalized events
    /// </summary>
    Channel<TelemetryEvent> OutputChannel { get; }

    /// <summary>
    /// Start processing
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop processing
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

