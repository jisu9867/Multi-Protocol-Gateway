using System.Diagnostics;
using Gateway.Core.Models;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// Kafka-specific metrics exporter
/// Tracks message processing rates and latencies
/// </summary>
public sealed class KafkaMetrics
{
    private static readonly Counter KafkaMessagesProcessedTotal = Metrics.CreateCounter(
        "kafka_messages_processed_total",
        "Total number of Kafka messages processed",
        new[] { "consumer_group", "topic", "status" }); // status: success, error

    private static readonly Histogram KafkaProcessingDuration = Metrics.CreateHistogram(
        "kafka_processing_duration_seconds",
        "Kafka message processing duration in seconds",
        new[] { "consumer_group", "topic" },
        new HistogramConfiguration
        {
            Buckets = new[] { 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0, 5.0 }
        });

    private static readonly Counter KafkaProducerMessagesTotal = Metrics.CreateCounter(
        "kafka_producer_messages_total",
        "Total number of messages produced to Kafka",
        new[] { "topic", "status" }); // status: success, error

    private static readonly Histogram KafkaProducerDuration = Metrics.CreateHistogram(
        "kafka_producer_duration_seconds",
        "Kafka producer send duration in seconds",
        new[] { "topic" },
        new HistogramConfiguration
        {
            Buckets = new[] { 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0 }
        });

    /// <summary>
    /// Record a successfully processed Kafka message
    /// </summary>
    public static void RecordMessageProcessed(string consumerGroup, string topic, TimeSpan duration)
    {
        KafkaMessagesProcessedTotal.WithLabels(consumerGroup, topic, "success").Inc();
        KafkaProcessingDuration.WithLabels(consumerGroup, topic).Observe(duration.TotalSeconds);
    }

    /// <summary>
    /// Record a failed Kafka message processing
    /// </summary>
    public static void RecordMessageError(string consumerGroup, string topic)
    {
        KafkaMessagesProcessedTotal.WithLabels(consumerGroup, topic, "error").Inc();
    }

    /// <summary>
    /// Record a successfully produced message
    /// </summary>
    public static void RecordMessageProduced(string topic, TimeSpan duration)
    {
        KafkaProducerMessagesTotal.WithLabels(topic, "success").Inc();
        KafkaProducerDuration.WithLabels(topic).Observe(duration.TotalSeconds);
    }

    /// <summary>
    /// Record a failed message production
    /// </summary>
    public static void RecordProduceError(string topic)
    {
        KafkaProducerMessagesTotal.WithLabels(topic, "error").Inc();
    }
}

