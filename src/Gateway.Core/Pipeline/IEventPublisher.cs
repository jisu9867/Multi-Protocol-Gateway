using System.Threading.Channels;
using Gateway.Core.Models;

namespace Gateway.Core.Pipeline;

/// <summary>
/// Output port for publishing normalized telemetry events.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Input channel for normalized events.
    /// </summary>
    Channel<TelemetryEvent> InputChannel { get; }

    /// <summary>
    /// Start publisher processing.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop publisher processing.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
