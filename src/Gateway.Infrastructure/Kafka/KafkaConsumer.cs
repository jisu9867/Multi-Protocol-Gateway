using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Confluent.Kafka;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gateway.Infrastructure.Kafka;

/// <summary>
/// Kafka Consumer that reads telemetry events from Kafka and forwards to sinks
/// </summary>
public sealed class KafkaConsumer : BackgroundService
{
    private readonly ILogger<KafkaConsumer> _logger;
    private readonly KafkaOptions _options;
    private readonly Channel<TelemetryEvent> _outputChannel;
    private IConsumer<string, string>? _consumer;

    public KafkaConsumer(
        ILogger<KafkaConsumer> logger,
        IOptions<KafkaOptions> kafkaOptions)
    {
        _logger = logger;
        _options = kafkaOptions.Value;

        var channelOptions = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _outputChannel = Channel.CreateBounded<TelemetryEvent>(channelOptions);
    }

    public Channel<TelemetryEvent> OutputChannel => _outputChannel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                GroupId = _options.ConsumerGroupId,
                AutoOffsetReset = Enum.Parse<AutoOffsetReset>(_options.Consumer.AutoOffsetReset, ignoreCase: true),
                EnableAutoCommit = _options.Consumer.EnableAutoCommit,
                FetchMinBytes = 1,
                FetchMaxBytes = 1024 * 1024, // 1MB
                SessionTimeoutMs = _options.Consumer.SessionTimeoutMs,
                EnableAutoOffsetStore = _options.Consumer.EnableAutoOffsetStore,
                SocketTimeoutMs = 60000,
                // Allow auto-creation of topics in development (requires broker config: auto.create.topics.enable=true)
                AllowAutoCreateTopics = true
            };

            // Apply Event Hubs configuration if connection string is provided
            if (!string.IsNullOrWhiteSpace(_options.EventHubsConnectionString))
            {
                var eventHubsConfig = EventHubsHelper.ParseEventHubsConnectionString(_options.EventHubsConnectionString);
                EventHubsHelper.ApplyEventHubsConfig(config, eventHubsConfig);
                
                // Log Event Hub name if specified
                if (eventHubsConfig.TryGetValue("eventhub.name", out var eventHubName))
                {
                    _logger.LogInformation("Using Event Hub name from connection string as topic: {EventHubName}", eventHubName);
                }
            }

            var consumerBuilder = new ConsumerBuilder<string, string>(config);
            _consumer = consumerBuilder.Build();

            // Wait a bit for Kafka/Event Hubs to be ready
            await Task.Delay(2000, stoppingToken).ConfigureAwait(false);

            // Use Event Hub name as topic if available, otherwise use configured topic
            var topic = !string.IsNullOrWhiteSpace(_options.EventHubsConnectionString) 
                ? GetEventHubNameFromConnectionString() ?? _options.Topic
                : _options.Topic;
            
            _consumer.Subscribe(topic);

            var serverInfo = !string.IsNullOrWhiteSpace(_options.EventHubsConnectionString) 
                ? "Azure Event Hubs" 
                : _options.BootstrapServers;
            
            _logger.LogInformation("Kafka Consumer started (Server: {Server}, Topic: {Topic}, GroupId: {GroupId})",
                serverInfo, _options.Topic, _options.ConsumerGroupId);
            
            _logger.LogDebug("Kafka Consumer configuration: AutoOffsetReset={AutoOffsetReset}, EnableAutoCommit={EnableAutoCommit}",
                _options.Consumer.AutoOffsetReset, _options.Consumer.EnableAutoCommit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Kafka Consumer. Error: {Message}", ex.Message);
            throw;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);

                    if (consumeResult?.Message == null)
                    {
                        continue;
                    }

                    try
                    {
                        var telemetryEvent = DeserializeMessage(consumeResult.Message);
                        if (telemetryEvent != null)
                        {
                            await _outputChannel.Writer.WriteAsync(telemetryEvent, stoppingToken).ConfigureAwait(false);

                            _logger.LogDebug("Consumed telemetry event {EventId} from {Service} topic {Topic}, partition {Partition}, offset {Offset}",
                                telemetryEvent.EventId,
                                !string.IsNullOrWhiteSpace(_options.EventHubsConnectionString) ? "Event Hub" : "Kafka",
                                consumeResult.Topic, consumeResult.Partition, consumeResult.Offset);

                            // Manual commit if auto-commit is disabled
                            if (!_options.Consumer.EnableAutoCommit)
                            {
                                _consumer.Commit(consumeResult);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Failed to deserialize message from Kafka topic {Topic}, partition {Partition}, offset {Offset}",
                                consumeResult.Topic, consumeResult.Partition, consumeResult.Offset);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize message from Kafka topic {Topic}, partition {Partition}, offset {Offset}",
                            consumeResult.Topic, consumeResult.Partition, consumeResult.Offset);
                        // Continue processing other messages
                    }
                }
                catch (ConsumeException ex)
                {
                    // Handle UnknownTopicOrPartition error more gracefully
                    // Check error code by string comparison for compatibility
                    var errorCode = ex.Error.Code.ToString();
                    if (errorCode.Contains("UnknownTopicOrPartition") || 
                        errorCode.Contains("UnknownTopic") ||
                        ex.Error.Code == (ErrorCode)3) // ErrorCode.UnknownTopicOrPartition = 3
                    {
                        _logger.LogWarning(
                            "Kafka topic '{Topic}' does not exist yet. " +
                            "This is normal if no messages have been produced yet. " +
                            "Will retry in 5 seconds. " +
                            "To create the topic manually, run: kafka-topics --create --topic {Topic} --bootstrap-server {BootstrapServers}",
                            _options.Topic, _options.Topic, _options.BootstrapServers);
                        await Task.Delay(5000, stoppingToken).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogError(ex, "Error consuming from Kafka: {Error}, ErrorCode: {ErrorCode}. Will retry in 1 second.",
                            ex.Error.Reason, ex.Error.Code);
                        await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (KafkaException ex)
                {
                    _logger.LogError(ex, "Kafka exception: {Message}. Will retry in 1 second.", ex.Message);
                    await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kafka Consumer error");
            throw;
        }
        finally
        {
            _consumer?.Close();
            _outputChannel.Writer.Complete();
            _logger.LogInformation("Kafka Consumer stopped");
        }
    }

    private TelemetryEvent? DeserializeMessage(Message<string, string> message)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var telemetryEvent = JsonSerializer.Deserialize<TelemetryEvent>(message.Value, options);
            return telemetryEvent;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Kafka message. Value: {Value}", message.Value);
            return null;
        }
    }

    private string? GetEventHubNameFromConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_options.EventHubsConnectionString))
        {
            return null;
        }
        
        var eventHubsConfig = EventHubsHelper.ParseEventHubsConnectionString(_options.EventHubsConnectionString);
        return eventHubsConfig.TryGetValue("eventhub.name", out var eventHubName) ? eventHubName : null;
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}

