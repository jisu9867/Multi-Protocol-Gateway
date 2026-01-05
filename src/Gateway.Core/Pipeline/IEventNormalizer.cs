using Gateway.Core.Models;

namespace Gateway.Core.Pipeline;

/// <summary>
/// Event normalization interface
/// Converts raw adapter data into canonical TelemetryEvent format
/// </summary>
public interface IEventNormalizer
{
    /// <summary>
    /// Normalize raw data into a TelemetryEvent
    /// </summary>
    /// <param name="raw">Raw data from adapter (type depends on adapter protocol)</param>
    /// <returns>Normalized TelemetryEvent</returns>
    TelemetryEvent Normalize(object raw);
}

