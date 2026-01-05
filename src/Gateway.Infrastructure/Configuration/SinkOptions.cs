namespace Gateway.Infrastructure.Configuration;

/// <summary>
/// Configuration options for sinks
/// </summary>
public sealed class SinkOptions
{
    public const string SectionName = "Sinks";

    /// <summary>
    /// PostgreSQL sink configuration
    /// </summary>
    public PostgreSqlSinkOptions PostgreSql { get; set; } = new();

    /// <summary>
    /// JSONL file sink configuration
    /// </summary>
    public JsonlFileSinkOptions JsonlFile { get; set; } = new();
}

/// <summary>
/// Configuration options for PostgreSQL sink
/// </summary>
public sealed class PostgreSqlSinkOptions
{
    /// <summary>
    /// Batch size for bulk insert operations
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Flush interval in milliseconds. Events are flushed when this interval elapses even if batch is not full.
    /// </summary>
    public int FlushIntervalMs { get; set; } = 1000;
}

/// <summary>
/// Configuration options for JSONL file sink
/// </summary>
public sealed class JsonlFileSinkOptions
{
    /// <summary>
    /// Base directory path for JSONL files. Files will be created as logs/events-YYYYMMDD.jsonl
    /// </summary>
    public string BasePath { get; set; } = "logs";

    /// <summary>
    /// Batch size for buffering before flushing to file
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Flush interval in milliseconds. Events are flushed when this interval elapses even if batch is not full.
    /// </summary>
    public int FlushIntervalMs { get; set; } = 2000;
}

