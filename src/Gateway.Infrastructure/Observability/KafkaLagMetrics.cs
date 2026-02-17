using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Confluent.Kafka;
using Gateway.Infrastructure.Configuration;
using Gateway.Infrastructure.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// Kafka Consumer Lag metrics collector using OpenTelemetry Meter
/// Calculates lag by comparing high watermark (latest offset) with consumer group committed offset
/// </summary>
public sealed class KafkaLagMetrics : IDisposable
{
    private readonly ILogger<KafkaLagMetrics> _logger;
    private readonly KafkaOptions _options;
    private readonly Timer _updateTimer;
    private readonly ConcurrentDictionary<string, ConsumerGroupLagInfo> _lagInfo = new();
    
    private static readonly Meter Meter = new("Gateway.Kafka.Lag", "1.0.0");

    // Store current metric values for ObservableGauge
    private readonly ConcurrentDictionary<string, long> _lagValues = new();
    private readonly ConcurrentDictionary<string, long> _committedOffsetValues = new();
    private readonly ConcurrentDictionary<string, long> _highWatermarkValues = new();

    public KafkaLagMetrics(
        ILogger<KafkaLagMetrics> logger,
        IOptions<KafkaOptions> kafkaOptions)
    {
        _logger = logger;
        _options = kafkaOptions.Value;
        
        // Register observable gauges with measurement callbacks
        Meter.CreateObservableGauge("kafka_consumer_lag", GetLagMeasurements, "messages", "Kafka consumer lag");
        Meter.CreateObservableGauge("kafka_consumer_committed_offset", GetCommittedOffsetMeasurements, "offset", "Committed offset");
        Meter.CreateObservableGauge("kafka_consumer_high_watermark", GetHighWatermarkMeasurements, "offset", "High watermark");
        
        // Update lag metrics every 10 seconds
        _updateTimer = new Timer(UpdateLagMetrics, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Register a consumer group for lag monitoring
    /// </summary>
    public void RegisterConsumerGroup(string consumerGroupId, string topic)
    {
        var key = $"{consumerGroupId}:{topic}";
        _lagInfo.TryAdd(key, new ConsumerGroupLagInfo
        {
            ConsumerGroupId = consumerGroupId,
            Topic = topic
        });
        _logger.LogInformation("Registered consumer group {GroupId} for topic {Topic} for lag monitoring", 
            consumerGroupId, topic);
    }

    private void UpdateLagMetrics(object? state)
    {
        try
        {
            foreach (var kvp in _lagInfo)
            {
                var lagInfo = kvp.Value;
                UpdateLagForConsumerGroup(lagInfo.ConsumerGroupId, lagInfo.Topic);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Kafka lag metrics");
        }
    }

    private void UpdateLagForConsumerGroup(string consumerGroupId, string topic)
    {
        try
        {
            // Create admin client to query offsets
            var adminConfig = new AdminClientConfig
            {
                BootstrapServers = _options.BootstrapServers
            };

            // Apply Event Hubs configuration if provided
            if (!string.IsNullOrWhiteSpace(_options.EventHubsConnectionString))
            {
                var eventHubsConfig = EventHubsHelper.ParseEventHubsConnectionString(_options.EventHubsConnectionString);
                EventHubsHelper.ApplyEventHubsConfig(adminConfig, eventHubsConfig);
            }

            using var adminClient = new AdminClientBuilder(adminConfig).Build();

            // Get topic metadata to find partitions
            var metadata = adminClient.GetMetadata(topic, TimeSpan.FromSeconds(10));
            var partitions = metadata.Topics.FirstOrDefault(t => t.Topic == topic)?.Partitions ?? new List<PartitionMetadata>();

            if (!partitions.Any())
            {
                _logger.LogDebug("No partitions found for topic {Topic}", topic);
                return;
            }

            // For each partition, calculate lag
            foreach (var partition in partitions)
            {
                try
                {
                    // Get high watermark (latest offset)
                    using var consumerForWatermark = CreateConsumerForWatermark();
                    var watermark = consumerForWatermark.QueryWatermarkOffsets(
                        new TopicPartition(topic, partition.PartitionId),
                        TimeSpan.FromSeconds(5));
                    
                    var highWatermark = watermark.High.Value;
                    var highWatermarkKey = $"{topic}:{partition.PartitionId}";
                    _highWatermarkValues.AddOrUpdate(highWatermarkKey, highWatermark, (k, v) => highWatermark);

                    // Get committed offset for consumer group
                    var committedOffsets = consumerForWatermark.Committed(
                        new[] { new TopicPartition(topic, partition.PartitionId) },
                        TimeSpan.FromSeconds(5));

                    var committedOffset = committedOffsets.FirstOrDefault();
                    if (committedOffset != null)
                    {
                        var offset = committedOffset.Offset.Value;
                        var committedOffsetKey = $"{consumerGroupId}:{topic}:{partition.PartitionId}";
                        _committedOffsetValues.AddOrUpdate(committedOffsetKey, offset, (k, v) => offset);

                        // Calculate lag: high watermark - committed offset
                        // Note: If committed offset is -1 (no commit yet), lag = high watermark
                        var lag = offset >= 0 ? highWatermark - offset : highWatermark;
                        var lagKey = $"{consumerGroupId}:{topic}:{partition.PartitionId}";
                        _lagValues.AddOrUpdate(lagKey, lag, (k, v) => lag);

                        _logger.LogDebug(
                            "Consumer Group {GroupId}, Topic {Topic}, Partition {Partition}: " +
                            "High Watermark={HighWatermark}, Committed Offset={CommittedOffset}, Lag={Lag}",
                            consumerGroupId, topic, partition.PartitionId, highWatermark, offset, lag);
                    }
                    else
                    {
                        // No committed offset yet - lag equals high watermark
                        var lagKey = $"{consumerGroupId}:{topic}:{partition.PartitionId}";
                        _lagValues.AddOrUpdate(lagKey, highWatermark, (k, v) => highWatermark);
                        _logger.LogDebug(
                            "Consumer Group {GroupId}, Topic {Topic}, Partition {Partition}: " +
                            "No committed offset, Lag={Lag} (equals high watermark)",
                            consumerGroupId, topic, partition.PartitionId, highWatermark);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, 
                        "Failed to get lag for Consumer Group {GroupId}, Topic {Topic}, Partition {Partition}",
                        consumerGroupId, topic, partition.PartitionId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to update lag metrics for Consumer Group {GroupId}, Topic {Topic}",
                consumerGroupId, topic);
        }
    }

    private IEnumerable<Measurement<long>> GetLagMeasurements()
    {
        foreach (var kvp in _lagValues)
        {
            var parts = kvp.Key.Split(':');
            if (parts.Length == 3)
            {
                var consumerGroup = parts[0];
                var topic = parts[1];
                var partition = parts[2];
                
                yield return new Measurement<long>(
                    kvp.Value,
                    new TagList
                    {
                        { "consumer_group", consumerGroup },
                        { "topic", topic },
                        { "partition", partition }
                    });
            }
        }
    }

    private IEnumerable<Measurement<long>> GetCommittedOffsetMeasurements()
    {
        foreach (var kvp in _committedOffsetValues)
        {
            var parts = kvp.Key.Split(':');
            if (parts.Length == 3)
            {
                var consumerGroup = parts[0];
                var topic = parts[1];
                var partition = parts[2];
                
                yield return new Measurement<long>(
                    kvp.Value,
                    new TagList
                    {
                        { "consumer_group", consumerGroup },
                        { "topic", topic },
                        { "partition", partition }
                    });
            }
        }
    }

    private IEnumerable<Measurement<long>> GetHighWatermarkMeasurements()
    {
        foreach (var kvp in _highWatermarkValues)
        {
            var parts = kvp.Key.Split(':');
            if (parts.Length == 2)
            {
                var topic = parts[0];
                var partition = parts[1];
                
                yield return new Measurement<long>(
                    kvp.Value,
                    new TagList
                    {
                        { "topic", topic },
                        { "partition", partition }
                    });
            }
        }
    }

    private IConsumer<string, string> CreateConsumerForWatermark()
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = $"lag-monitor-{Guid.NewGuid()}", // Temporary group for watermark queries
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        // Apply Event Hubs configuration if provided
        if (!string.IsNullOrWhiteSpace(_options.EventHubsConnectionString))
        {
            var eventHubsConfig = EventHubsHelper.ParseEventHubsConnectionString(_options.EventHubsConnectionString);
            EventHubsHelper.ApplyEventHubsConfig(config, eventHubsConfig);
        }

        return new ConsumerBuilder<string, string>(config).Build();
    }

    public void Dispose()
    {
        _updateTimer?.Dispose();
    }

    private sealed class ConsumerGroupLagInfo
    {
        public string ConsumerGroupId { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
    }
}
