using System.Threading.Channels;
using Gateway.Core.Models;

namespace Gateway.Core.Pipeline;

/// <summary>
/// Routing stage - routes normalized events to sinks
/// </summary>
public interface IRoute
{
    /// <summary>
    /// Input channel for normalized events
    /// </summary>
    Channel<TelemetryEvent> InputChannel { get; }

    /// <summary>
    /// Output channels for routed events to sinks
    /// </summary>
    IReadOnlyList<Channel<TelemetryEvent>> OutputChannels { get; }

    /// <summary>
    /// Start processing
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop processing
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

