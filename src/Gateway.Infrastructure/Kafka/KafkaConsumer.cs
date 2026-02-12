using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Confluent.Kafka;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Gateway.Infrastructure.Configuration;
using Gateway.Infrastructure.Observability;
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
    private readonly string _consumerGroupId;
    private readonly string? _autoOffsetReset;

    public KafkaConsumer(
        ILogger<KafkaConsumer> logger,
        IOptions<KafkaOptions> kafkaOptions,
        string? consumerGroupId = null,
        string? autoOffsetReset = null)
    {
        _logger = logger;
        _options = kafkaOptions.Value;
        _consumerGroupId = consumerGroupId ?? _options.ConsumerGroupId;
        _autoOffsetReset = autoOffsetReset ?? _options.Consumer.AutoOffsetReset;

        var channelOptions = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _outputChannel = Channel.CreateBounded<TelemetryEvent>(channelOptions);
    }

    public Channel<TelemetryEvent> OutputChannel => _outputChannel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Kafka Consumer [{GroupId}]: ExecuteAsync called - starting consumer", _consumerGroupId);
        
        try
        {
            _logger.LogInformation("Kafka Consumer [{GroupId}]: Creating consumer configuration...", _consumerGroupId);
            var config = new ConsumerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                GroupId = _consumerGroupId,
                AutoOffsetReset = Enum.Parse<AutoOffsetReset>(_autoOffsetReset!, ignoreCase: true),
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
                serverInfo, _options.Topic, _consumerGroupId);
            
            _logger.LogInformation("Kafka Consumer configuration: AutoOffsetReset={AutoOffsetReset}, EnableAutoCommit={EnableAutoCommit}, GroupId={GroupId}",
                _autoOffsetReset, _options.Consumer.EnableAutoCommit, _consumerGroupId);
            
            _logger.LogInformation("Kafka Consumer [{GroupId}]: Starting to consume messages from topic {Topic}...", 
                _consumerGroupId, _options.Topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Kafka Consumer. Error: {Message}", ex.Message);
            throw;
        }

        try
        {
            _logger.LogInformation("Kafka Consumer [{GroupId}]: Entering message consumption loop", _consumerGroupId);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Kafka Consumer [{GroupId}]: Waiting for message from Kafka...", _consumerGroupId);
                    var consumeResult = _consumer.Consume(stoppingToken);

                    if (consumeResult?.Message == null)
                    {
                        _logger.LogDebug("Kafka Consumer [{GroupId}]: Received null message, continuing...", _consumerGroupId);
                        continue;
                    }

                    _logger.LogInformation("Kafka Consumer [{GroupId}]: Received raw message from topic {Topic}, partition {Partition}, offset {Offset}, message length={MessageLength}", 
                        _consumerGroupId, consumeResult.Topic, consumeResult.Partition, consumeResult.Offset, consumeResult.Message.Value?.Length ?? 0);

                    var processingStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var telemetryEvent = DeserializeMessage(consumeResult.Message);
                        if (telemetryEvent != null)
                        {
                            _logger.LogInformation("Kafka Consumer [{GroupId}]: Consumed telemetry event {EventId} (Factory={FactoryId}, Tag={Tag}, SourceId={SourceId}) from topic {Topic}, partition {Partition}, offset {Offset}",
                                _consumerGroupId,
                                telemetryEvent.EventId,
                                telemetryEvent.FactoryId,
                                telemetryEvent.Tag,
                                telemetryEvent.SourceId,
                                consumeResult.Topic, consumeResult.Partition, consumeResult.Offset);
                            
                            await _outputChannel.Writer.WriteAsync(telemetryEvent, stoppingToken).ConfigureAwait(false);
                            
                            processingStopwatch.Stop();
                            
                            // Record metrics
                            KafkaMetrics.RecordMessageProcessed(_consumerGroupId, consumeResult.Topic, processingStopwatch.Elapsed);
                            
                            _logger.LogInformation("Kafka Consumer [{GroupId}]: Successfully wrote event {EventId} to OutputChannel", _consumerGroupId, telemetryEvent.EventId);

                            // Manual commit if auto-commit is disabled
                            if (!_options.Consumer.EnableAutoCommit)
                            {
                                _consumer.Commit(consumeResult);
                            }
                        }
                        else
                        {
                            processingStopwatch.Stop();
                            KafkaMetrics.RecordMessageError(_consumerGroupId, consumeResult.Topic);
                            
                            _logger.LogWarning("Failed to deserialize message from Kafka topic {Topic}, partition {Partition}, offset {Offset}",
                                consumeResult.Topic, consumeResult.Partition, consumeResult.Offset);
                        }
                    }
                    catch (JsonException ex)
                    {
                        processingStopwatch.Stop();
                        KafkaMetrics.RecordMessageError(_consumerGroupId, consumeResult.Topic);
                        
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

