using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Gateway.Ui.Services;

/// <summary>
/// Service for managing SignalR connection and real-time telemetry data
/// </summary>
public class SignalRService : IAsyncDisposable
{
    private readonly ILogger<SignalRService> _logger;
    private HubConnection? _hubConnection;
    private readonly string _hubUrl;

    public SignalRService(ILogger<SignalRService> logger, IConfiguration configuration)
    {
        _logger = logger;
        var apiBaseUrl = configuration["GatewayApi:BaseUrl"] ?? "http://localhost:5011";
        if (apiBaseUrl.EndsWith("/"))
        {
            apiBaseUrl = apiBaseUrl.TrimEnd('/');
        }
        _hubUrl = $"{apiBaseUrl}/hubs/telemetry";
    }

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public async Task StartAsync()
    {
        if (_hubConnection != null && IsConnected)
        {
            _logger.LogDebug("SignalR connection already established");
            return;
        }

        _logger.LogInformation("Initializing SignalR connection to {HubUrl}", _hubUrl);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning("SignalR connection lost. Reconnecting... Error: {Error}", error?.Message ?? "Unknown");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += connectionId =>
        {
            _logger.LogInformation("SignalR reconnected successfully. Connection ID: {ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        _hubConnection.Closed += error =>
        {
            if (error != null)
            {
                _logger.LogError(error, "SignalR connection closed with error");
            }
            else
            {
                _logger.LogWarning("SignalR connection closed");
            }
            return Task.CompletedTask;
        };

        try
        {
            _logger.LogInformation("Attempting to start SignalR connection...");
            await _hubConnection.StartAsync();
            _logger.LogInformation("SignalR connection started successfully. Hub URL: {HubUrl}, State: {State}", 
                _hubUrl, _hubConnection.State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start SignalR connection to {HubUrl}. Error: {ErrorMessage}", 
                _hubUrl, ex.Message);
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    /// <summary>
    /// Subscribe to real-time updates for a specific sensor
    /// </summary>
    public async Task SubscribeToSensorAsync(string factoryId, string tag, string? sourceId = null)
    {
        if (_hubConnection == null || !IsConnected)
        {
            await StartAsync();
        }

        if (!IsConnected)
        {
            _logger.LogError("Cannot subscribe: SignalR connection is not established");
            throw new InvalidOperationException("SignalR connection is not established");
        }

        await _hubConnection!.InvokeAsync("SubscribeToSensor", factoryId, tag, sourceId);
        _logger.LogInformation("Subscribed to sensor: Factory={FactoryId}, Tag={Tag}, SourceId={SourceId}", 
            factoryId, tag, sourceId ?? "all");
    }

    /// <summary>
    /// Unsubscribe from sensor updates
    /// </summary>
    public async Task UnsubscribeFromSensorAsync(string factoryId, string tag, string? sourceId = null)
    {
        if (_hubConnection == null || !IsConnected)
        {
            return;
        }

        await _hubConnection.InvokeAsync("UnsubscribeFromSensor", factoryId, tag, sourceId);
        _logger.LogDebug("Unsubscribed from sensor: Factory={FactoryId}, Tag={Tag}, SourceId={SourceId}", factoryId, tag, sourceId);
    }

    private readonly List<Action<TelemetryEventDto>> _callbacks = new();

    /// <summary>
    /// Register a callback for receiving telemetry events
    /// </summary>
    public void OnReceiveTelemetryEvent(Action<TelemetryEventDto> callback)
    {
        if (_hubConnection == null)
        {
            throw new InvalidOperationException("Hub connection is not initialized. Call StartAsync first.");
        }

        // Add callback to list
        if (!_callbacks.Contains(callback))
        {
            _callbacks.Add(callback);
        }

        // Set up handler only once
        if (_callbacks.Count == 1)
        {
            _hubConnection.On<object>("ReceiveTelemetryEvent", (data) =>
            {
                try
                {
                    TelemetryEventDto? dto = null;
                    
                    // Handle JsonElement or already deserialized object
                    if (data is JsonElement jsonElement)
                    {
                        dto = JsonSerializer.Deserialize<TelemetryEventDto>(jsonElement.GetRawText(), new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    else if (data is string jsonString)
                    {
                        dto = JsonSerializer.Deserialize<TelemetryEventDto>(jsonString, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    else
                    {
                        // Try to serialize and deserialize
                        var serialized = JsonSerializer.Serialize(data);
                        dto = JsonSerializer.Deserialize<TelemetryEventDto>(serialized, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    
                    if (dto != null)
                    {
                        _logger.LogInformation("SignalR Client: Received telemetry event: Factory={FactoryId}, Tag={Tag}, SourceId={SourceId}, Value={Value}", 
                            dto.FactoryId, dto.Tag, dto.SourceId, dto.Value);
                        
                        // Call all registered callbacks
                        foreach (var cb in _callbacks)
                        {
                            try
                            {
                                cb(dto);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error in callback");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to deserialize telemetry event: {Data}", data);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing telemetry event: {Data}", data);
                }
            });
        }
    }

    /// <summary>
    /// Remove a callback
    /// </summary>
    public void RemoveCallback(Action<TelemetryEventDto> callback)
    {
        _callbacks.Remove(callback);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}

/// <summary>
/// DTO for telemetry events received from SignalR
/// </summary>
public class TelemetryEventDto
{
    public Guid EventId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string FactoryId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Quality { get; set; } = string.Empty;
    public long Sequence { get; set; }
}

