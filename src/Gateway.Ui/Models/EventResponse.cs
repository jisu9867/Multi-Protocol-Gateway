using System.Text.Json;

namespace Gateway.Ui.Models;

/// <summary>
/// Event response from Gateway API
/// </summary>
public sealed class EventResponse
{
    public DateTime Timestamp { get; set; }
    public Guid EventId { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public JsonElement Value { get; set; }
    public string RouteKey { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public long Sequence { get; set; }
    public string Quality { get; set; } = string.Empty;
}

