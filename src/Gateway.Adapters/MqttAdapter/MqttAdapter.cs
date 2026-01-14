using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gateway.Core.Adapters;
using Gateway.Adapters.FakeAdapter;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace Gateway.Adapters.MqttAdapter;

/// <summary>
/// MQTT adapter that subscribes to factory/+/telemetry topics
/// </summary>
public sealed class MqttAdapter : IAdapter, IAsyncDisposable
{
    private static readonly ActivitySource ActivitySource = new("Gateway.Adapters.MqttAdapter");
    
    private readonly ILogger<MqttAdapter> _logger;
    private readonly string _id;
    private readonly MqttAdapterOptions _options;
    private readonly IAdapterDataHandler? _dataHandler;
    
    private IMqttClient? _mqttClient;
    private Task? _connectionTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private AdapterStatus _status = AdapterStatus.Stopped;
    
    // Metrics
    private long _reconnectCount = 0;
    private long _messageCount = 0;
    private DateTime _lastMessageTime = DateTime.MinValue;
    private readonly object _metricsLock = new();
    
    // Message deduplication: track recently processed messages
    private readonly HashSet<string> _recentlyProcessedMessages = new();
    private readonly object _deduplicationLock = new();
    private const int MaxDeduplicationCacheSize = 1000;
    
    // Circuit breaker state
    private CircuitState _circuitState = CircuitState.Closed;
    private DateTime _circuitOpenTime = DateTime.MinValue;
    private int _failureCount = 0;
    private const int MaxFailures = 5;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromSeconds(30);
    
    private enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }

    public MqttAdapter(
        string id,
        MqttAdapterOptions options,
        ILogger<MqttAdapter> logger,
        IAdapterDataHandler? dataHandler = null)
    {
        _id = id;
        _options = options;
        _logger = logger;
        _dataHandler = dataHandler;
    }

    public string Id => _id;
    public AdapterStatus Status => _status;
    public IAdapterDataHandler? DataHandler { get; set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_status == AdapterStatus.Running)
        {
            return Task.CompletedTask;
        }

        _status = AdapterStatus.Starting;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectionTask = ConnectAndSubscribeAsync(_cancellationTokenSource.Token);
        _status = AdapterStatus.Running;

        _logger.LogInformation("MqttAdapter {AdapterId} started", _id);
        
        // Don't await - run in background
        _ = _connectionTask.ContinueWith(_ => { }, TaskContinuationOptions.None);
        
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_status != AdapterStatus.Running)
        {
            return;
        }

        _status = AdapterStatus.Stopping;

        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
        }

        if (_mqttClient != null && _mqttClient.IsConnected)
        {
            try
            {
                await _mqttClient.DisconnectAsync(
                    new MqttClientDisconnectOptions { Reason = MqttClientDisconnectOptionsReason.NormalDisconnection },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting MQTT client");
            }
        }

        if (_connectionTask != null)
        {
            try
            {
                await _connectionTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _mqttClient?.Dispose();
        _cancellationTokenSource?.Dispose();
        _connectionTask = null;
        _mqttClient = null;
        _status = AdapterStatus.Stopped;

        _logger.LogInformation("MqttAdapter {AdapterId} stopped", _id);
    }

    public Task<AdapterHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        lock (_metricsLock)
        {
            var messageRate = CalculateMessageRate();
            var isConnected = _mqttClient?.IsConnected ?? false;
            
            // Consider connected if:
            // 1. Status is Running AND (client is connected OR we've received messages recently)
            // 2. If we've received messages, we're definitely connected
            var isActuallyConnected = _status == AdapterStatus.Running && 
                (isConnected || _messageCount > 0 || (DateTime.UtcNow - _lastMessageTime).TotalSeconds < 60);
            
            var health = new AdapterHealth
            {
                Status = _status,
                Metrics = new Dictionary<string, object>
                {
                    ["status"] = _status.ToString(),
                    ["reconnect_count"] = _reconnectCount,
                    ["message_rate"] = messageRate,
                    ["message_count"] = _messageCount,
                    ["circuit_state"] = _circuitState.ToString(),
                    ["is_connected"] = isActuallyConnected,
                    ["client_connected"] = isConnected,
                    ["last_message_seconds_ago"] = _lastMessageTime == DateTime.MinValue ? -1 : (DateTime.UtcNow - _lastMessageTime).TotalSeconds
                }
            };

            if (_status == AdapterStatus.Faulted)
            {
                health.ErrorMessage = "Adapter is in faulted state";
            }
            else if (_circuitState == CircuitState.Open)
            {
                health.ErrorMessage = $"Circuit breaker is open (failures: {_failureCount})";
            }
            else if (!isActuallyConnected && _status == AdapterStatus.Running)
            {
                // Only show error if we're supposed to be running but not connected
                // Give it some time - if status is Running but no messages yet, wait a bit
                if (_messageCount == 0 && (DateTime.UtcNow - _lastMessageTime).TotalSeconds > 120)
                {
                    health.ErrorMessage = "MQTT client is not connected";
                }
                // Otherwise, if status is Running, assume it's connecting or just started
            }

            return Task.FromResult(health);
        }
    }

    private async Task ConnectAndSubscribeAsync(CancellationToken cancellationToken)
    {
        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _mqttClient.ConnectedAsync += OnConnectedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

        var retryDelay = TimeSpan.FromSeconds(1); // Initial retry delay
        const int maxRetryDelaySeconds = 60;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Check circuit breaker
                if (_circuitState == CircuitState.Open)
                {
                    if (DateTime.UtcNow - _circuitOpenTime < CircuitOpenDuration)
                    {
                        _logger.LogWarning("Circuit breaker is open, waiting before retry");
                        await Task.Delay(CircuitOpenDuration, cancellationToken).ConfigureAwait(false);
                        _circuitState = CircuitState.HalfOpen;
                        _failureCount = 0;
                    }
                    else
                    {
                        _circuitState = CircuitState.HalfOpen;
                        _failureCount = 0;
                    }
                }

                var mqttClientOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer(_options.Server, _options.Port)
                    .WithClientId(_options.ClientId ?? $"gateway-{_id}-{Guid.NewGuid():N}")
                    .WithCredentials(_options.Username, _options.Password)
                    .WithCleanSession()
                    .Build();

                _logger.LogInformation("Connecting to MQTT broker {Server}:{Port}", _options.Server, _options.Port);
                
                var connectResult = await _mqttClient.ConnectAsync(mqttClientOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
                {
                    _logger.LogInformation("Connected to MQTT broker successfully");

                    // Subscribe to topic
                    var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(f =>
                        {
                            f.WithTopic(_options.Topic ?? "factory/+/telemetry");
                        })
                        .Build();

                    var subscribeResult = await _mqttClient.SubscribeAsync(subscribeOptions, cancellationToken)
                        .ConfigureAwait(false);

                    _logger.LogInformation("Subscribed to topic: {Topic}", _options.Topic ?? "factory/+/telemetry");

                    // Reset retry delay on successful connection
                    retryDelay = TimeSpan.FromSeconds(1);
                    _circuitState = CircuitState.Closed;
                    _failureCount = 0;

                    // Wait for disconnection (or cancellation)
                    await WaitForDisconnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning("Failed to connect to MQTT broker: {ResultCode}", connectResult.ResultCode);
                    RecordFailure();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to MQTT broker");
                RecordFailure();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Exponential backoff
            Interlocked.Increment(ref _reconnectCount);
            _logger.LogInformation("Retrying connection in {Delay} seconds (attempt {Attempt})", 
                retryDelay.TotalSeconds, _reconnectCount);
            
            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            
            // Exponential backoff: double the delay, but cap at maxRetryDelaySeconds
            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, maxRetryDelaySeconds));
        }
    }

    private void RecordFailure()
    {
        _failureCount++;
        if (_failureCount >= MaxFailures)
        {
            _circuitState = CircuitState.Open;
            _circuitOpenTime = DateTime.UtcNow;
            _logger.LogWarning("Circuit breaker opened after {Failures} failures", _failureCount);
        }
    }

    private async Task WaitForDisconnectionAsync(CancellationToken cancellationToken)
    {
        // Wait until disconnected or cancelled
        while (_mqttClient?.IsConnected == true && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs arg)
    {
        _logger.LogInformation("MQTT client connected");
        _circuitState = CircuitState.Closed;
        _failureCount = 0;
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        if (arg.Reason == MqttClientDisconnectReason.NormalDisconnection)
        {
            _logger.LogInformation("MQTT client disconnected normally");
        }
        else
        {
            _logger.LogWarning("MQTT client disconnected unexpectedly: {Reason}", arg.Reason);
            RecordFailure();
        }
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        try
        {
            var topic = arg.ApplicationMessage.Topic;
            var payload = arg.ApplicationMessage.ConvertPayloadToString();

            _logger.LogDebug("Received MQTT message on topic {Topic}", topic);

            using var activity = ActivitySource.StartActivity("MqttAdapter.ReceiveMessage");
            activity?.SetTag("mqtt.topic", topic);
            activity?.SetTag("adapter.id", _id);

            // Parse JSON payload
            var jsonDoc = JsonDocument.Parse(payload);
            var root = jsonDoc.RootElement;

            // Extract fields from payload: { "sourceId": "...", "tag": "...", "value": ..., "ts": "..." }
            if (!root.TryGetProperty("sourceId", out var sourceIdProp) ||
                !root.TryGetProperty("tag", out var tagProp) ||
                !root.TryGetProperty("value", out var valueProp))
            {
                _logger.LogWarning("Invalid MQTT message format - missing required fields");
                return;
            }

            var sourceId = sourceIdProp.GetString() ?? "unknown";
            var tag = tagProp.GetString() ?? "unknown";
            var value = valueProp;
            
            // Create message ID for deduplication (topic + sourceId + tag + value + timestamp)
            var messageId = CreateMessageId(topic, sourceId, tag, value.GetRawText(), root);
            
            // Check for duplicate message BEFORE processing (critical: must be done first to prevent concurrent processing)
            bool shouldProcess;
            lock (_deduplicationLock)
            {
                if (_recentlyProcessedMessages.Contains(messageId))
                {
                    _logger.LogInformation("Duplicate MQTT message detected and ignored: {MessageId} (topic: {Topic}, sourceId: {SourceId}, tag: {Tag})", 
                        messageId, topic, sourceId, tag);
                    return;
                }
                
                // Add to deduplication cache immediately to prevent concurrent processing
                _recentlyProcessedMessages.Add(messageId);
                shouldProcess = true;
                
                // Limit cache size to prevent memory leak
                if (_recentlyProcessedMessages.Count > MaxDeduplicationCacheSize)
                {
                    // Remove oldest entries (simple approach: clear and rebuild)
                    // In production, consider using a more sophisticated approach like LRU cache
                    _recentlyProcessedMessages.Clear();
                    _logger.LogDebug("Deduplication cache cleared due to size limit");
                }
            }
            
            if (!shouldProcess)
            {
                return;
            }
            
            _logger.LogDebug("Processing MQTT message: {MessageId} (topic: {Topic}, sourceId: {SourceId}, tag: {Tag})", 
                messageId, topic, sourceId, tag);

            // Parse timestamp (ts field) or use current time
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            if (root.TryGetProperty("ts", out var tsProp))
            {
                var tsStr = tsProp.GetString();
                if (!string.IsNullOrEmpty(tsStr))
                {
                    if (DateTimeOffset.TryParse(tsStr, out var parsedTs))
                    {
                        timestamp = parsedTs.ToUniversalTime();
                    }
                    else if (DateTime.TryParse(tsStr, out var parsedDt))
                    {
                        // If parsing as DateTime succeeds, assume UTC if no timezone info
                        timestamp = new DateTimeOffset(parsedDt, TimeSpan.Zero).ToUniversalTime();
                    }
                }
            }

            // Extract topic segment (e.g., from "factory/line-1/telemetry" extract "line-1")
            var topicSegments = topic.Split('/');
            var deviceId = topicSegments.Length > 1 ? topicSegments[1] : "unknown";

            // Build payload dictionary
            var payloadDict = new Dictionary<string, object>
            {
                ["value"] = value.GetRawText() // Keep as JSON string for flexibility
            };

            // Build metadata
            var metadata = new Dictionary<string, string>
            {
                ["tag"] = tag,
                ["topic"] = topic,
                ["deviceId"] = deviceId
            };

            // Extract factory_id from payload if present
            if (root.TryGetProperty("factoryId", out var factoryIdProp))
            {
                var factoryId = factoryIdProp.GetString();
                if (!string.IsNullOrEmpty(factoryId))
                {
                    metadata["factory_id"] = factoryId;
                }
            }

            // Extract equipment_type from payload if present
            if (root.TryGetProperty("equipmentType", out var equipmentTypeProp))
            {
                var equipmentType = equipmentTypeProp.GetString();
                if (!string.IsNullOrEmpty(equipmentType))
                {
                    metadata["equipment_type"] = equipmentType;
                }
            }

            // Extract equipment_name from payload if present
            if (root.TryGetProperty("equipmentName", out var equipmentNameProp))
            {
                var equipmentName = equipmentNameProp.GetString();
                if (!string.IsNullOrEmpty(equipmentName))
                {
                    metadata["equipment_name"] = equipmentName;
                }
            }

            // Extract sequence from payload if present
            if (root.TryGetProperty("seq", out var seqProp))
            {
                if (seqProp.ValueKind == JsonValueKind.Number && seqProp.TryGetInt64(out var seq))
                {
                    metadata["sequence"] = seq.ToString();
                }
            }

            var traceId = Activity.Current?.Id ?? activity?.Id;
            if (!string.IsNullOrEmpty(traceId))
            {
                metadata["traceId"] = traceId;
            }
            // Also check for traceId in payload
            else if (root.TryGetProperty("traceId", out var traceIdProp))
            {
                var payloadTraceId = traceIdProp.GetString();
                if (!string.IsNullOrEmpty(payloadTraceId))
                {
                    metadata["traceId"] = payloadTraceId;
                }
            }

            var handler = _dataHandler ?? DataHandler;
            if (handler != null)
            {
                await handler.HandleDataAsync(
                    _id,
                    sourceId,
                    timestamp.DateTime, // Convert DateTimeOffset to DateTime (UTC)
                    payloadDict,
                    metadata,
                    CancellationToken.None).ConfigureAwait(false);

                // Update metrics
                lock (_metricsLock)
                {
                    Interlocked.Increment(ref _messageCount);
                    _lastMessageTime = DateTime.UtcNow;
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse MQTT message as JSON");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT message");
        }
    }

    private double CalculateMessageRate()
    {
        lock (_metricsLock)
        {
            if (_lastMessageTime == DateTime.MinValue)
            {
                return 0;
            }

            var elapsed = DateTime.UtcNow - _lastMessageTime;
            if (elapsed.TotalSeconds > 60)
            {
                // No messages in the last minute
                return 0;
            }

            // Simple rate: messages per minute (would need sliding window for accurate rate)
            return _messageCount / Math.Max(elapsed.TotalMinutes, 1);
        }
    }

    private string CreateMessageId(string topic, string sourceId, string tag, string valueJson, JsonElement root)
    {
        // Create a hash-based ID from topic, sourceId, tag, and value only
        // Note: We exclude timestamp to catch duplicate processing of the same message
        // This will prevent the same message from being processed twice even if received at different times
        var messageContent = $"{topic}|{sourceId}|{tag}|{valueJson}";
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(messageContent));
        return Convert.ToBase64String(hashBytes);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _mqttClient?.Dispose();
        _cancellationTokenSource?.Dispose();
        
        lock (_deduplicationLock)
        {
            _recentlyProcessedMessages.Clear();
        }
    }
}
