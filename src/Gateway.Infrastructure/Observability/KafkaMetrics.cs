using System.Diagnostics;
using System.Diagnostics.Metrics;
using Gateway.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// Kafka-specific metrics exporter using OpenTelemetry Meter
/// Tracks message processing rates and latencies
/// </summary>
public sealed class KafkaMetrics
{
    private static readonly Meter Meter = new("Gateway.Kafka", "1.0.0");
    
    private static readonly Counter<long> KafkaMessagesProcessedTotal = Meter.CreateCounter<long>(
        "kafka_messages_processed_total",
        "messages",
        "Total number of Kafka messages processed");

    private static readonly Histogram<double> KafkaProcessingDuration = Meter.CreateHistogram<double>(
        "kafka_processing_duration_seconds",
        "seconds",
        "Kafka message processing duration in seconds");

    private static readonly Counter<long> KafkaProducerMessagesTotal = Meter.CreateCounter<long>(
        "kafka_producer_messages_total",
        "messages",
        "Total number of messages produced to Kafka");

    private static readonly Histogram<double> KafkaProducerDuration = Meter.CreateHistogram<double>(
        "kafka_producer_duration_seconds",
        "seconds",
        "Kafka producer send duration in seconds");

    /// <summary>
    /// Record a successfully processed Kafka message
    /// </summary>
    public static void RecordMessageProcessed(string consumerGroup, string topic, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "consumer_group", consumerGroup },
            { "topic", topic },
            { "status", "success" }
        };
        KafkaMessagesProcessedTotal.Add(1, tags);
        
        var latencyTags = new TagList
        {
            { "consumer_group", consumerGroup },
            { "topic", topic }
        };
        KafkaProcessingDuration.Record(duration.TotalSeconds, latencyTags);
    }

    /// <summary>
    /// Record a failed Kafka message processing
    /// </summary>
    public static void RecordMessageError(string consumerGroup, string topic)
    {
        var tags = new TagList
        {
            { "consumer_group", consumerGroup },
            { "topic", topic },
            { "status", "error" }
        };
        KafkaMessagesProcessedTotal.Add(1, tags);
    }

    /// <summary>
    /// Record a successfully produced message
    /// </summary>
    public static void RecordMessageProduced(string topic, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "topic", topic },
            { "status", "success" }
        };
        KafkaProducerMessagesTotal.Add(1, tags);
        
        var latencyTags = new TagList
        {
            { "topic", topic }
        };
        KafkaProducerDuration.Record(duration.TotalSeconds, latencyTags);
    }

    /// <summary>
    /// Record a failed message production
    /// </summary>
    public static void RecordProduceError(string topic)
    {
        var tags = new TagList
        {
            { "topic", topic },
            { "status", "error" }
        };
        KafkaProducerMessagesTotal.Add(1, tags);
    }

    /// <summary>
    /// Seed Kafka metrics so Grafana panels do not show "No data" before live traffic arrives.
    /// </summary>
    public static void InitializeMetrics()
    {
        var topic = "telemetry-events";
        var consumerGroup = "gateway-observability-seed";

        // Process side
        RecordMessageProcessed(consumerGroup, topic, TimeSpan.FromMilliseconds(5));
        RecordMessageError(consumerGroup, topic);

        // Producer side
        RecordMessageProduced(topic, TimeSpan.FromMilliseconds(3));
        RecordProduceError(topic);
    }
}
