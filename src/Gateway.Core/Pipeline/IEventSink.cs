using Gateway.Core.Models;

namespace Gateway.Core.Pipeline;

/// <summary>
/// Event sink interface for persisting events
/// </summary>
public interface IEventSink
{
    /// <summary>
    /// Write an event to the sink
    /// </summary>
    /// <param name="routeKey">Route key indicating which route this event belongs to</param>
    /// <param name="event">Telemetry event to persist</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the write operation</returns>
    Task WriteAsync(RouteKey routeKey, TelemetryEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Write an event to the sink with string route key
    /// </summary>
    /// <param name="routeKey">Route key as string</param>
    /// <param name="event">Telemetry event to persist</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the write operation</returns>
    Task WriteAsync(string routeKey, TelemetryEvent @event, CancellationToken cancellationToken = default);
}

