using Gateway.Core.Models;
using Gateway.Api.Hubs;
using Gateway.Infrastructure.Kafka;
using Microsoft.AspNetCore.SignalR;
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
        KafkaConsumer kafkaConsumer)
    {
        _logger = logger;
        _hubContext = hubContext;
        _kafkaConsumer = kafkaConsumer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SignalR Telemetry Service started");

        try
        {
            await foreach (var telemetryEvent in _kafkaConsumer.OutputChannel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await BroadcastTelemetryEvent(telemetryEvent, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error broadcasting telemetry event {EventId}", telemetryEvent.EventId);
                }
            }
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
        var groupName = $"{telemetryEvent.FactoryId}:{telemetryEvent.Tag}";
        await _hubContext.Clients.Group(groupName).SendAsync("ReceiveTelemetryEvent", eventDto, cancellationToken);

        // Also broadcast to clients subscribed to specific sourceId
        var specificGroupName = $"{telemetryEvent.FactoryId}:{telemetryEvent.Tag}:{telemetryEvent.SourceId}";
        await _hubContext.Clients.Group(specificGroupName).SendAsync("ReceiveTelemetryEvent", eventDto, cancellationToken);

        _logger.LogInformation("Broadcasted telemetry event {EventId} (Factory={FactoryId}, Tag={Tag}, SourceId={SourceId}, Value={Value}) to groups {GroupName} and {SpecificGroupName}",
            telemetryEvent.EventId, telemetryEvent.FactoryId, telemetryEvent.Tag, telemetryEvent.SourceId, value, groupName, specificGroupName);
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

