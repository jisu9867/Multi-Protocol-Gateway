using Gateway.Core.Models;

namespace Gateway.Core.Pipeline;

/// <summary>
/// Event routing interface
/// Determines routing key for events based on event properties
/// </summary>
public interface IEventRouter
{
    /// <summary>
    /// Route an event and return a route key
    /// </summary>
    /// <param name="event">Telemetry event to route</param>
    /// <returns>Route key (string or enum)</returns>
    RouteKey Route(TelemetryEvent @event);

    /// <summary>
    /// Route an event and return a string route key
    /// </summary>
    /// <param name="event">Telemetry event to route</param>
    /// <returns>Route key as string</returns>
    string RouteToString(TelemetryEvent @event) => Route(@event).ToString();
}

