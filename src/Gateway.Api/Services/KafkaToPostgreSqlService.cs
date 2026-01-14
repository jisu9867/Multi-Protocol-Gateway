using System.Threading.Channels;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Gateway.Infrastructure.Kafka;
using Gateway.Infrastructure.Sinks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Api.Services;

/// <summary>
/// Service that connects Kafka Consumer to PostgreSQL Sink
/// </summary>
public sealed class KafkaToPostgreSqlService : BackgroundService
{
    private readonly ILogger<KafkaToPostgreSqlService> _logger;
    private readonly KafkaConsumer _kafkaConsumer;
    private readonly PostgreSqlSink _postgreSqlSink;

    public KafkaToPostgreSqlService(
        ILogger<KafkaToPostgreSqlService> logger,
        KafkaConsumer kafkaConsumer,
        PostgreSqlSink postgreSqlSink)
    {
        _logger = logger;
        _kafkaConsumer = kafkaConsumer;
        _postgreSqlSink = postgreSqlSink;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Starting Kafka to PostgreSQL connection service");

        // Wait a bit for Kafka Consumer to initialize
        await Task.Delay(3000, stoppingToken).ConfigureAwait(false);

        try
        {
            // Connect Kafka Consumer -> PostgreSQL Sink
            await foreach (var telemetryEvent in _kafkaConsumer.OutputChannel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await _postgreSqlSink.InputChannel.Writer.WriteAsync(telemetryEvent, stoppingToken)
                        .ConfigureAwait(false);
                    
                    _logger.LogDebug("Forwarded telemetry event {EventId} from Kafka to PostgreSQL sink",
                        telemetryEvent.EventId);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error forwarding telemetry event {EventId} from Kafka to PostgreSQL",
                        telemetryEvent.EventId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kafka to PostgreSQL service error: {Message}", ex.Message);
            throw;
        }
        finally
        {
            _logger.LogDebug("Kafka to PostgreSQL connection service stopped");
        }
    }
}

