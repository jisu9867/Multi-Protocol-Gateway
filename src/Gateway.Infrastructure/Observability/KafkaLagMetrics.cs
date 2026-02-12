using System.Collections.Concurrent;
using Confluent.Kafka;
using Gateway.Infrastructure.Configuration;
using Gateway.Infrastructure.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prometheus;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// Kafka Consumer Lag metrics collector
/// Calculates lag by comparing high watermark (latest offset) with consumer group committed offset
/// </summary>
public sealed class KafkaLagMetrics : IDisposable
{
    private readonly ILogger<KafkaLagMetrics> _logger;
    private readonly KafkaOptions _options;
    private readonly Timer _updateTimer;
    private readonly ConcurrentDictionary<string, ConsumerGroupLagInfo> _lagInfo = new();
    
    // Prometheus metrics
    private static readonly Gauge KafkaConsumerLag = Metrics.CreateGauge(
        "kafka_consumer_lag",
        "Kafka consumer lag (high watermark - committed offset) per consumer group and partition",
        new[] { "consumer_group", "topic", "partition" });
    
    private static readonly Gauge KafkaConsumerCommittedOffset = Metrics.CreateGauge(
        "kafka_consumer_committed_offset",
        "Committed offset per consumer group, topic, and partition",
        new[] { "consumer_group", "topic", "partition" });
    
    private static readonly Gauge KafkaConsumerHighWatermark = Metrics.CreateGauge(
        "kafka_consumer_high_watermark",
        "High watermark (latest available offset) per topic and partition",
        new[] { "topic", "partition" });

    public KafkaLagMetrics(
        ILogger<KafkaLagMetrics> logger,
        IOptions<KafkaOptions> kafkaOptions)
    {
        _logger = logger;
        _options = kafkaOptions.Value;
        
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
                    KafkaConsumerHighWatermark.WithLabels(topic, partition.PartitionId.ToString()).Set(highWatermark);

                    // Get committed offset for consumer group
                    var committedOffsets = consumerForWatermark.Committed(
                        new[] { new TopicPartition(topic, partition.PartitionId) },
                        TimeSpan.FromSeconds(5));

                    var committedOffset = committedOffsets.FirstOrDefault();
                    if (committedOffset != null)
                    {
                        var offset = committedOffset.Offset.Value;
                        KafkaConsumerCommittedOffset.WithLabels(consumerGroupId, topic, partition.PartitionId.ToString()).Set(offset);

                        // Calculate lag: high watermark - committed offset
                        // Note: If committed offset is -1 (no commit yet), lag = high watermark
                        var lag = offset >= 0 ? highWatermark - offset : highWatermark;
                        KafkaConsumerLag.WithLabels(consumerGroupId, topic, partition.PartitionId.ToString()).Set(lag);

                        _logger.LogDebug(
                            "Consumer Group {GroupId}, Topic {Topic}, Partition {Partition}: " +
                            "High Watermark={HighWatermark}, Committed Offset={CommittedOffset}, Lag={Lag}",
                            consumerGroupId, topic, partition.PartitionId, highWatermark, offset, lag);
                    }
                    else
                    {
                        // No committed offset yet - lag equals high watermark
                        KafkaConsumerLag.WithLabels(consumerGroupId, topic, partition.PartitionId.ToString()).Set(highWatermark);
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

