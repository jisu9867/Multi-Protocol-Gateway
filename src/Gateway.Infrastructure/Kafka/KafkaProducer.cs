using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Confluent.Kafka;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Gateway.Infrastructure.Configuration;
using Gateway.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gateway.Infrastructure.Kafka;

/// <summary>
/// Kafka Producer that sends telemetry events to Kafka topic
/// </summary>
public sealed class KafkaProducer : IEventPublisher, IAsyncDisposable
{
    private readonly ILogger<KafkaProducer> _logger;
    private readonly KafkaOptions _options;
    private readonly Channel<TelemetryEvent> _inputChannel;
    private IProducer<string, string>? _producer;
    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;

    public KafkaProducer(
        ILogger<KafkaProducer> logger,
        IOptions<KafkaOptions> kafkaOptions)
    {
        _logger = logger;
        _options = kafkaOptions.Value;

        var channelOptions = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _inputChannel = Channel.CreateBounded<TelemetryEvent>(channelOptions);
    }

    public Channel<TelemetryEvent> InputChannel => _inputChannel;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_producer != null)
        {
            return Task.CompletedTask;
        }

        try
        {
            var config = new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                EnableIdempotence = _options.Producer.EnableIdempotence,
                Acks = _options.Producer.Acks == "all" ? Acks.All : 
                       _options.Producer.Acks == "1" ? Acks.Leader : Acks.None,
                MessageSendMaxRetries = _options.Producer.Retries,
                BatchSize = _options.Producer.BatchSize,
                LingerMs = _options.Producer.LingerMs,
                SocketTimeoutMs = 60000,
                RequestTimeoutMs = 30000
            };

            ApplySecuritySettings(config);

            var producerBuilder = new ProducerBuilder<string, string>(config);
            _producer = producerBuilder.Build();

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _processingTask = ProcessAsync(_cancellationTokenSource.Token);

            _logger.LogInformation("Kafka Producer started (Server: {Server}, Topic: {Topic}, SecurityProtocol: {SecurityProtocol})",
                _options.BootstrapServers, _options.Topic, _options.SecurityProtocol ?? "Plaintext");
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Kafka Producer. Error: {Message}", ex.Message);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_producer == null)
        {
            return;
        }

        _inputChannel.Writer.Complete();

        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
        }

        try
        {
            if (_processingTask != null)
            {
                await _processingTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Flush remaining messages
        _producer?.Flush(TimeSpan.FromSeconds(5));

        _cancellationTokenSource?.Dispose();
        _processingTask = null;

        _logger.LogInformation("Kafka Producer stopped");
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await foreach (var telemetryEvent in _inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await PublishAsync(telemetryEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing telemetry event {EventId} to Kafka. Error: {Message}. StackTrace: {StackTrace}",
                    telemetryEvent.EventId, ex.Message, ex.StackTrace);
                // Continue processing other events
            }
        }

    }

    private async Task PublishAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        if (_producer == null)
        {
            _logger.LogError("Kafka producer is not initialized. Cannot publish event {EventId}", telemetryEvent.EventId);
            throw new InvalidOperationException("Kafka producer is not initialized");
        }

        // Serialize telemetry event to JSON
        var json = JsonSerializer.Serialize(telemetryEvent, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Use event_id as key for partitioning (ensures same event goes to same partition)
        var key = telemetryEvent.EventId.ToString();

        try
        {
            var message = new Message<string, string>
            {
                Key = key,
                Value = json,
                Headers = new Headers
                {
                    { "factory_id", Encoding.UTF8.GetBytes(((int)telemetryEvent.FactoryId).ToString()) },
                    { "equipment_type", Encoding.UTF8.GetBytes(telemetryEvent.EquipmentType) },
                    { "timestamp", Encoding.UTF8.GetBytes(telemetryEvent.Timestamp.ToUnixTimeMilliseconds().ToString()) }
                }
            };

            var topic = _options.Topic;
            
            var stopwatch = Stopwatch.StartNew();
            var deliveryResult = await _producer.ProduceAsync(topic, message, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();

            // Record metrics
            KafkaMetrics.RecordMessageProduced(topic, stopwatch.Elapsed);

            _logger.LogDebug("Kafka produced EventId={EventId} Topic={Topic} Partition={Partition} Offset={Offset}", telemetryEvent.EventId, deliveryResult.Topic, deliveryResult.Partition, deliveryResult.Offset);
        }
        catch (ProduceException<string, string> ex)
        {
            // Record error metrics
            var topic = _options.Topic;
            KafkaMetrics.RecordProduceError(topic);
            
            _logger.LogError(ex, "Failed to publish telemetry event {EventId} to Kafka. Error: {Error}, ErrorCode: {ErrorCode}",
                telemetryEvent.EventId, ex.Error.Reason, ex.Error.Code);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error publishing telemetry event {EventId} to Kafka", telemetryEvent.EventId);
            throw;
        }
    }

    private void ApplySecuritySettings(ProducerConfig config)
    {
        if (string.IsNullOrWhiteSpace(_options.SecurityProtocol))
        {
            return;
        }

        if (Enum.TryParse<SecurityProtocol>(_options.SecurityProtocol, true, out var securityProtocol))
        {
            config.SecurityProtocol = securityProtocol;
        }

        if (!string.IsNullOrWhiteSpace(_options.SaslMechanism) &&
            Enum.TryParse<SaslMechanism>(_options.SaslMechanism, true, out var saslMechanism))
        {
            config.SaslMechanism = saslMechanism;
        }

        if (!string.IsNullOrWhiteSpace(_options.SaslUsername))
        {
            config.SaslUsername = _options.SaslUsername;
        }

        if (!string.IsNullOrWhiteSpace(_options.SaslPassword))
        {
            config.SaslPassword = _options.SaslPassword;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _producer?.Dispose();
    }
}
