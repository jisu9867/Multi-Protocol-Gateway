using System.Net.Http.Json;
using System.Text.Json;
using Gateway.Ui.Models;

namespace Gateway.Ui.Services;

/// <summary>
/// Typed HTTP client for Gateway API
/// </summary>
public sealed class GatewayApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GatewayApiClient> _logger;

    public GatewayApiClient(HttpClient httpClient, ILogger<GatewayApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Get health status from Gateway API
    /// </summary>
    public async Task<HealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            // Use JsonDocument to parse and handle flexible types
            var jsonDocument = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            
            var root = jsonDocument.RootElement;
            var healthResponse = new HealthResponse
            {
                Status = root.GetProperty("status").GetString() ?? "unknown",
                LastEventTimestamp = root.TryGetProperty("lastEventTimestamp", out var lastEventProp) 
                    ? lastEventProp.GetString() 
                    : null
            };
            
            // Parse queueLengths
            if (root.TryGetProperty("queueLengths", out var queueLengthsProp))
            {
                healthResponse.QueueLengths = new Dictionary<string, int>();
                foreach (var prop in queueLengthsProp.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        healthResponse.QueueLengths[prop.Name] = prop.Value.GetInt32();
                    }
                }
            }
            
            // Parse adapters
            if (root.TryGetProperty("adapters", out var adaptersProp) && adaptersProp.ValueKind == JsonValueKind.Array)
            {
                var adapters = new List<AdapterStatus>();
                foreach (var adapterElement in adaptersProp.EnumerateArray())
                {
                    var adapter = new AdapterStatus
                    {
                        Id = adapterElement.GetProperty("id").GetString() ?? string.Empty,
                        Status = adapterElement.GetProperty("status").GetString() ?? string.Empty
                    };
                    
                    if (adapterElement.TryGetProperty("health", out var healthProp))
                    {
                        adapter.Health = new AdapterHealthInfo
                        {
                            StatusElement = healthProp.TryGetProperty("status", out var statusProp) ? statusProp : default,
                            ErrorMessage = healthProp.TryGetProperty("errorMessage", out var errorProp) ? errorProp.GetString() : null
                        };
                        
                        if (healthProp.TryGetProperty("metrics", out var metricsProp))
                        {
                            adapter.Health.Metrics = new Dictionary<string, JsonElement>();
                            foreach (var metricProp in metricsProp.EnumerateObject())
                            {
                                adapter.Health.Metrics[metricProp.Name] = metricProp.Value;
                            }
                        }
                    }
                    
                    adapters.Add(adapter);
                }
                healthResponse.Adapters = adapters.ToArray();
            }
            
            return healthResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching health status");
            return null;
        }
    }

    /// <summary>
    /// Get recent events from Gateway API
    /// </summary>
    public async Task<EventResponse[]?> GetRecentEventsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/events/recent?limit={limit}", cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            // Parse JSON manually to handle flexible types
            var jsonDocument = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            
            var events = new List<EventResponse>();
            foreach (var element in jsonDocument.RootElement.EnumerateArray())
            {
                var evt = new EventResponse
                {
                    EventId = element.GetProperty("eventId").GetGuid(),
                    Timestamp = element.GetProperty("timestamp").GetDateTime(),
                    SourceId = element.GetProperty("sourceId").GetString() ?? string.Empty,
                    Tag = element.GetProperty("tag").GetString() ?? string.Empty,
                    RouteKey = element.GetProperty("routeKey").GetString() ?? string.Empty,
                    Sequence = element.GetProperty("sequence").GetInt64(),
                    TraceId = element.TryGetProperty("traceId", out var traceIdProp) ? traceIdProp.GetString() : null
                };
                
                // Handle value (can be string or object)
                if (element.TryGetProperty("value", out var valueProp))
                {
                    evt.Value = valueProp.ValueKind == JsonValueKind.String
                        ? JsonDocument.Parse(valueProp.GetString() ?? "null").RootElement
                        : valueProp;
                }
                
                // Handle quality (can be number or string)
                if (element.TryGetProperty("quality", out var qualityProp))
                {
                    evt.Quality = qualityProp.ValueKind == JsonValueKind.String
                        ? qualityProp.GetString() ?? "0"
                        : qualityProp.GetInt32().ToString();
                }
                else
                {
                    evt.Quality = "0";
                }
                
                events.Add(evt);
            }
            
            return events.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching recent events");
            return null;
        }
    }
}
