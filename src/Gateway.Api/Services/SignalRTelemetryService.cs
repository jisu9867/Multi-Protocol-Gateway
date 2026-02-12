using System.Diagnostics;
using Gateway.Core.Models;
using Gateway.Api.Hubs;
using Gateway.Infrastructure.Kafka;
using Gateway.Infrastructure.Observability;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Api.Services;

/// <summary>
/// Service that forwards telemetry events from Kafka Consumer to SignalR clients
/// </summary>
public class SignalRTelemetryService : BackgroundService
{
    private readonly ILogger<SignalRTelemetryService> _logger;
    private readonly IHubContext<TelemetryHub> _hubContext;
    private readonly KafkaConsumer _kafkaConsumer;

    public SignalRTelemetryService(
        ILogger<SignalRTelemetryService> logger,
        IHubContext<TelemetryHub> hubContext,
        [FromKeyedServices("SignalR")] KafkaConsumer kafkaConsumer)
    {
        _logger = logger;
        _hubContext = hubContext;
        _kafkaConsumer = kafkaConsumer ?? throw new ArgumentNullException(nameof(kafkaConsumer));
        
        _logger.LogInformation("SignalRTelemetryService initialized with KafkaConsumer. OutputChannel is available: {HasChannel}", 
            _kafkaConsumer.OutputChannel != null);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SignalR Telemetry Service started (using separate Kafka Consumer Group)");
        _logger.LogInformation("KafkaConsumer instance: {ConsumerType}, OutputChannel: {HasChannel}", 
            _kafkaConsumer.GetType().Name, _kafkaConsumer.OutputChannel != null);
        
        _logger.LogInformation("Waiting for Kafka Consumer to initialize...");

        // Wait a bit for Kafka Consumer to initialize
        await Task.Delay(5000, stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("Starting to read from SignalR Kafka Consumer OutputChannel...");
        
        if (_kafkaConsumer == null)
        {
            _logger.LogError("KafkaConsumer is null! Cannot read messages.");
            return;
        }
        
        var outputChannel = _kafkaConsumer.OutputChannel;
        if (outputChannel == null)
        {
            _logger.LogError("OutputChannel is null! Cannot read messages.");
            return;
        }
        
        var reader = outputChannel.Reader;
        if (reader == null)
        {
            _logger.LogError("OutputChannel Reader is null! Cannot read messages.");
            return;
        }
        
        _logger.LogInformation("OutputChannel Reader available: {HasReader}", true);
        
        // Check if channel is already completed
        if (reader.Completion.IsCompleted)
        {
            _logger.LogWarning("OutputChannel Reader is already completed! This means the Kafka Consumer may have stopped.");
        }

        try
        {
            _logger.LogInformation("Waiting for messages from Kafka Consumer...");
            await foreach (var telemetryEvent in reader.ReadAllAsync(stoppingToken))
            {
                var broadcastStopwatch = Stopwatch.StartNew();
                try
                {
                    _logger.LogInformation("SignalR: Received event {EventId} (Factory={FactoryId}, Tag={Tag}, SourceId={SourceId}, Value={Value})", 
                        telemetryEvent.EventId, telemetryEvent.FactoryId, telemetryEvent.Tag, telemetryEvent.SourceId, ExtractValue(telemetryEvent.Value));
                    
                    await BroadcastTelemetryEvent(telemetryEvent, stoppingToken);
                    
                    broadcastStopwatch.Stop();
                    var factoryIdStr = telemetryEvent.FactoryId.ToString();
                    SignalRMetrics.RecordMessageSent(factoryIdStr, telemetryEvent.Tag, broadcastStopwatch.Elapsed);
                }
                catch (Exception ex)
                {
                    broadcastStopwatch.Stop();
                    var factoryIdStr = telemetryEvent.FactoryId.ToString();
                    SignalRMetrics.RecordSendError(factoryIdStr, telemetryEvent.Tag);
                    
                    _logger.LogError(ex, "Error broadcasting telemetry event {EventId}", telemetryEvent.EventId);
                }
            }
            
            _logger.LogWarning("SignalR Kafka Consumer OutputChannel reader completed - no more messages will be received");
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR Telemetry Service error");
            throw;
        }
        finally
        {
            _logger.LogInformation("SignalR Telemetry Service stopped");
        }
    }

    private async Task BroadcastTelemetryEvent(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        // Extract value from JsonElement
        var value = ExtractValue(telemetryEvent.Value);
        
        // Create DTO for client
        var eventDto = new
        {
            eventId = telemetryEvent.EventId,
            timestamp = telemetryEvent.Timestamp,
            factoryId = telemetryEvent.FactoryId.ToString(),
            sourceId = telemetryEvent.SourceId,
            equipmentType = telemetryEvent.EquipmentType,
            equipmentName = telemetryEvent.EquipmentName,
            tag = telemetryEvent.Tag,
            value = value,
            quality = telemetryEvent.Quality.ToString(),
            sequence = telemetryEvent.Sequence
        };

        // Broadcast to all clients subscribed to this factory + tag combination
        // Use consistent casing for FactoryId (convert to string first, then use as-is)
        var factoryIdStr = telemetryEvent.FactoryId.ToString();
        var groupName = $"{factoryIdStr}:{telemetryEvent.Tag}";
        var specificGroupName = $"{factoryIdStr}:{telemetryEvent.Tag}:{telemetryEvent.SourceId}";
        
        _logger.LogInformation("Broadcasting to groups: {GroupName} and {SpecificGroupName} (FactoryId enum={FactoryId}, string={FactoryIdStr})", 
            groupName, specificGroupName, telemetryEvent.FactoryId, factoryIdStr);
        
        // Broadcast to general group (factory + tag) - this receives ALL lines for this factory+tag
        await _hubContext.Clients.Group(groupName).SendAsync("ReceiveTelemetryEvent", eventDto, cancellationToken);
        _logger.LogInformation("Sent to group {GroupName} (all lines)", groupName);

        // Also broadcast to clients subscribed to specific sourceId (specific line)
        if (!string.IsNullOrEmpty(telemetryEvent.SourceId))
        {
            await _hubContext.Clients.Group(specificGroupName).SendAsync("ReceiveTelemetryEvent", eventDto, cancellationToken);
            _logger.LogInformation("Sent to group {SpecificGroupName} (specific line)", specificGroupName);
        }

        _logger.LogInformation("Broadcasted telemetry event {EventId} (Factory={FactoryId}, Tag={Tag}, SourceId={SourceId}, Value={Value})",
            telemetryEvent.EventId, telemetryEvent.FactoryId, telemetryEvent.Tag, telemetryEvent.SourceId, value);
    }

    private static double ExtractValue(System.Text.Json.JsonElement valueElement)
    {
        if (valueElement.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (valueElement.TryGetProperty("value", out var valueProp))
            {
                if (valueProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    return valueProp.GetDouble();
                }
                else if (valueProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    // Try to parse string as number
                    if (double.TryParse(valueProp.GetString(), out var parsedValue))
                    {
                        return parsedValue;
                    }
                }
            }
        }
        else if (valueElement.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            return valueElement.GetDouble();
        }
        else if (valueElement.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            // Try to parse string as number
            if (double.TryParse(valueElement.GetString(), out var parsedValue))
            {
                return parsedValue;
            }
        }

        return 0.0;
    }
}

