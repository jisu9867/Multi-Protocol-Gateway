namespace Gateway.Infrastructure.Configuration;

/// <summary>
/// Configuration options for Kafka
/// </summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>
    /// Kafka bootstrap servers (comma-separated list)
    /// For Azure Event Hubs, this will be auto-generated from EventHubsConnectionString if provided
    /// </summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Azure Event Hubs connection string (optional, takes precedence over BootstrapServers if provided)
    /// Format: Endpoint=sb://{namespace}.servicebus.windows.net/;SharedAccessKeyName={keyName};SharedAccessKey={key};EntityPath={eventHubName}
    /// </summary>
    public string? EventHubsConnectionString { get; set; }

    /// <summary>
    /// Topic name for telemetry events (Event Hub name for Azure Event Hubs)
    /// </summary>
    public string Topic { get; set; } = "telemetry-events";

    /// <summary>
    /// Consumer group ID
    /// </summary>
    public string ConsumerGroupId { get; set; } = "gateway-consumer-group";

    /// <summary>
    /// Producer configuration
    /// </summary>
    public KafkaProducerOptions Producer { get; set; } = new();

    /// <summary>
    /// Consumer configuration
    /// </summary>
    public KafkaConsumerOptions Consumer { get; set; } = new();
}

/// <summary>
/// Kafka Producer configuration options
/// </summary>
public sealed class KafkaProducerOptions
{
    /// <summary>
    /// Enable idempotent producer (ensures exactly-once semantics)
    /// </summary>
    public bool EnableIdempotence { get; set; } = true;

    /// <summary>
    /// Acks: 0 (no ack), 1 (leader ack), -1/all (all replicas ack)
    /// </summary>
    public string Acks { get; set; } = "all";

    /// <summary>
    /// Maximum number of retries
    /// </summary>
    public int Retries { get; set; } = 3;

    /// <summary>
    /// Batch size in bytes
    /// </summary>
    public int BatchSize { get; set; } = 16384;

    /// <summary>
    /// Linger time in milliseconds (wait time before sending batch)
    /// </summary>
    public int LingerMs { get; set; } = 10;
}

/// <summary>
/// Kafka Consumer configuration options
/// </summary>
public sealed class KafkaConsumerOptions
{
    /// <summary>
    /// Enable auto commit
    /// </summary>
    public bool EnableAutoCommit { get; set; } = false;

    /// <summary>
    /// Auto offset reset: earliest, latest, none
    /// </summary>
    public string AutoOffsetReset { get; set; } = "earliest";

    /// <summary>
    /// Maximum number of records to fetch in one poll
    /// </summary>
    public int MaxPollRecords { get; set; } = 100;

    /// <summary>
    /// Session timeout in milliseconds
    /// </summary>
    public int SessionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Enable auto offset commit (manual commit if false)
    /// </summary>
    public bool EnableAutoOffsetStore { get; set; } = false;
}

