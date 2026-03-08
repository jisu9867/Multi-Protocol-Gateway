namespace Gateway.Infrastructure.Configuration;

/// <summary>
/// Configuration options for Kafka
/// </summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>
    /// Kafka bootstrap servers (comma-separated list)
    /// </summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Security protocol. Example: Plaintext, Ssl, SaslPlaintext, SaslSsl
    /// </summary>
    public string? SecurityProtocol { get; set; }

    /// <summary>
    /// SASL mechanism. Example: Plain, ScramSha256, ScramSha512
    /// </summary>
    public string? SaslMechanism { get; set; }

    /// <summary>
    /// SASL username (for Event Hubs Kafka endpoint use "$ConnectionString")
    /// </summary>
    public string? SaslUsername { get; set; }

    /// <summary>
    /// SASL password (for Event Hubs Kafka endpoint use full Event Hubs connection string)
    /// </summary>
    public string? SaslPassword { get; set; }

    /// <summary>
    /// Topic name for telemetry events
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
