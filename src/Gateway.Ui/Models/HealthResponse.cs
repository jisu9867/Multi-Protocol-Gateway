using System.Text.Json;

namespace Gateway.Ui.Models;

/// <summary>
/// Health status response from Gateway API
/// </summary>
public sealed class HealthResponse
{
    public string Status { get; set; } = string.Empty;
    public AdapterStatus[] Adapters { get; set; } = Array.Empty<AdapterStatus>();
    public Dictionary<string, int> QueueLengths { get; set; } = new();
    public string? LastEventTimestamp { get; set; }
}

public sealed class AdapterStatus
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public AdapterHealthInfo Health { get; set; } = new();
}

public sealed class AdapterHealthInfo
{
    // Status can be a number (enum value) or string, so we use JsonElement for flexibility
    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public JsonElement StatusElement { get; set; }
    
    public string Status => StatusElement.ValueKind switch
    {
        JsonValueKind.String => StatusElement.GetString() ?? "Unknown",
        JsonValueKind.Number => StatusElement.GetInt32().ToString(),
        _ => "Unknown"
    };
    
    public string? ErrorMessage { get; set; }
    public Dictionary<string, JsonElement> Metrics { get; set; } = new();
}
