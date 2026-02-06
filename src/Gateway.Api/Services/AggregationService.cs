using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Gateway.Core.Models;
using Gateway.Infrastructure.Data;
using Gateway.Infrastructure.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Gateway.Api.Services;

/// <summary>
/// Service that aggregates telemetry events into 10-minute and 1-hour buckets
/// Consumes from Kafka and stores aggregated results in sensor_agg_10min and sensor_agg_1hour tables
/// </summary>
public sealed class AggregationService : BackgroundService
{
    private readonly ILogger<AggregationService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServiceProvider _serviceProvider;
    
    // In-memory aggregation buffers
    private readonly ConcurrentDictionary<string, AggregationStats> _stats10Min = new();
    private readonly ConcurrentDictionary<string, AggregationStats> _stats1Hour = new();
    
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly Timer? _flushTimer;
    private const int FlushIntervalSeconds = 10; // Flush every 10 seconds

    public AggregationService(
        ILogger<AggregationService> logger,
        IServiceScopeFactory scopeFactory,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _serviceProvider = serviceProvider;
        
        // Setup periodic flush timer
        _flushTimer = new Timer(OnFlushTimer, null, TimeSpan.FromSeconds(FlushIntervalSeconds), TimeSpan.FromSeconds(FlushIntervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AggregationService started. Flush interval: {Interval} seconds", FlushIntervalSeconds);

        try
        {
            // Get keyed Kafka Consumer for Aggregation
            var kafkaConsumer = _serviceProvider.GetRequiredKeyedService<KafkaConsumer>("Aggregation");
            
            // Start the consumer in the background (it's a BackgroundService but registered as KeyedService)
            // Use reflection to call ExecuteAsync since it's protected
            var executeAsyncMethod = typeof(KafkaConsumer).GetMethod("ExecuteAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (executeAsyncMethod == null)
            {
                throw new InvalidOperationException("Failed to find ExecuteAsync method on KafkaConsumer");
            }
            
            // Start consumer in background task
            var consumerTask = Task.Run(async () =>
            {
                try
                {
                    var task = (Task)executeAsyncMethod.Invoke(kafkaConsumer, new object[] { stoppingToken })!;
                    await task;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Aggregation Kafka Consumer");
                }
            }, stoppingToken);
            
            // Wait for Kafka Consumer to initialize
            await Task.Delay(3000, stoppingToken).ConfigureAwait(false);

            // Consume events from Kafka Consumer
            await foreach (var telemetryEvent in kafkaConsumer.OutputChannel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // Only aggregate good quality data
                    if (telemetryEvent.Quality != DataQuality.Good)
                    {
                        continue;
                    }

                    // Extract numeric value from JSON
                    var numericValue = ExtractNumericValue(telemetryEvent.Value);
                    if (numericValue == null)
                    {
                        continue;
                    }

                    var timestamp = telemetryEvent.Timestamp.UtcDateTime;
                    
                    // Calculate buckets
                    var bucket10Min = Calculate10MinBucket(timestamp);
                    var bucket1Hour = Calculate1HourBucket(timestamp);

                    // Update 10-minute aggregation
                    var key10Min = CreateKey(bucket10Min, telemetryEvent);
                    _stats10Min.AddOrUpdate(
                        key10Min,
                        new AggregationStats
                        {
                            Bucket = bucket10Min,
                            FactoryId = telemetryEvent.FactoryId,
                            Tag = telemetryEvent.Tag,
                            EquipmentType = telemetryEvent.EquipmentType,
                            EquipmentName = telemetryEvent.EquipmentName,
                            SourceId = telemetryEvent.SourceId,
                            Sum = numericValue.Value,
                            Min = numericValue.Value,
                            Max = numericValue.Value,
                            Count = 1,
                            LastTimestamp = timestamp
                        },
                        (key, existing) =>
                        {
                            existing.Sum += numericValue.Value;
                            existing.Min = Math.Min(existing.Min, numericValue.Value);
                            existing.Max = Math.Max(existing.Max, numericValue.Value);
                            existing.Count++;
                            existing.LastTimestamp = timestamp > existing.LastTimestamp ? timestamp : existing.LastTimestamp;
                            return existing;
                        });

                    // Update 1-hour aggregation
                    var key1Hour = CreateKey(bucket1Hour, telemetryEvent);
                    _stats1Hour.AddOrUpdate(
                        key1Hour,
                        new AggregationStats
                        {
                            Bucket = bucket1Hour,
                            FactoryId = telemetryEvent.FactoryId,
                            Tag = telemetryEvent.Tag,
                            EquipmentType = telemetryEvent.EquipmentType,
                            EquipmentName = telemetryEvent.EquipmentName,
                            SourceId = telemetryEvent.SourceId,
                            Sum = numericValue.Value,
                            Min = numericValue.Value,
                            Max = numericValue.Value,
                            Count = 1
                        },
                        (key, existing) =>
                        {
                            existing.Sum += numericValue.Value;
                            existing.Min = Math.Min(existing.Min, numericValue.Value);
                            existing.Max = Math.Max(existing.Max, numericValue.Value);
                            existing.Count++;
                            return existing;
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing telemetry event for aggregation: {EventId}", 
                        telemetryEvent.EventId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AggregationService error");
            throw;
        }
        finally
        {
            // Final flush on shutdown
            await FlushAggregationsAsync(CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("AggregationService stopped");
        }
    }

    private void OnFlushTimer(object? state)
    {
        // Fire and forget - don't await to avoid blocking timer thread
        _ = Task.Run(async () =>
        {
            try
            {
                await FlushAggregationsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in periodic flush");
            }
        });
    }

    private async Task FlushAggregationsAsync(CancellationToken cancellationToken)
    {
        if (!await _flushLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // Another flush is in progress, skip this one
            return;
        }

        try
        {
            // Snapshot current stats and clear buffers
            var stats10MinSnapshot = new Dictionary<string, AggregationStats>();
            var stats1HourSnapshot = new Dictionary<string, AggregationStats>();

            foreach (var kvp in _stats10Min)
            {
                stats10MinSnapshot[kvp.Key] = kvp.Value;
            }
            _stats10Min.Clear();

            foreach (var kvp in _stats1Hour)
            {
                stats1HourSnapshot[kvp.Key] = kvp.Value;
            }
            _stats1Hour.Clear();

            if (stats10MinSnapshot.Count == 0 && stats1HourSnapshot.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Flushing aggregations: {Count10Min} 10-min buckets, {Count1Hour} 1-hour buckets",
                stats10MinSnapshot.Count, stats1HourSnapshot.Count);

            using var scope = _scopeFactory.CreateScope();
            await using var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

            // Flush 10-minute aggregations
            if (stats10MinSnapshot.Count > 0)
            {
                await Flush10MinAggregationsAsync(dbContext, stats10MinSnapshot, cancellationToken).ConfigureAwait(false);
            }

            // Flush 1-hour aggregations
            if (stats1HourSnapshot.Count > 0)
            {
                await Flush1HourAggregationsAsync(dbContext, stats1HourSnapshot, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing aggregations");
            throw;
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private async Task Flush10MinAggregationsAsync(
        GatewayDbContext dbContext,
        Dictionary<string, AggregationStats> stats,
        CancellationToken cancellationToken)
    {
        var entities = stats.Values.Select(s => new SensorAggregation10MinEntity
        {
            Bucket = s.Bucket,
            FactoryId = s.FactoryId,
            Tag = s.Tag,
            EquipmentType = s.EquipmentType,
            EquipmentName = s.EquipmentName,
            SourceId = s.SourceId,
            AvgValue = s.Sum / s.Count,
            MinValue = s.Min,
            MaxValue = s.Max,
            Count = s.Count,
            LastTimestamp = s.LastTimestamp
        }).ToList();

        if (entities.Count == 0)
        {
            return;
        }

        // Use raw SQL for UPSERT (ON CONFLICT DO UPDATE)
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Batch UPSERT using PostgreSQL ON CONFLICT
            // Note: We store sum and count separately, then calculate avg on the fly
            // But since we're using avg_value column, we need to recalculate: (old_sum + new_sum) / (old_count + new_count)
            // For now, we'll store sum and count, and calculate avg in the query
            // Actually, we need to store sum separately or recalculate avg correctly
            // Let's use a workaround: store the sum in avg_value * count, then recalculate
            var sql = @"
                INSERT INTO sensor_agg_10min (bucket, factory_id, tag, equipment_type, equipment_name, source_id, avg_value, min_value, max_value, count, last_timestamp)
                VALUES (@bucket, @factory_id, @tag, @equipment_type, @equipment_name, @source_id, @avg_value, @min_value, @max_value, @count, @last_timestamp)
                ON CONFLICT (bucket, factory_id, tag, equipment_type, equipment_name, source_id)
                DO UPDATE SET
                    avg_value = (sensor_agg_10min.avg_value * sensor_agg_10min.count + EXCLUDED.avg_value * EXCLUDED.count) / (sensor_agg_10min.count + EXCLUDED.count),
                    min_value = LEAST(sensor_agg_10min.min_value, EXCLUDED.min_value),
                    max_value = GREATEST(sensor_agg_10min.max_value, EXCLUDED.max_value),
                    count = sensor_agg_10min.count + EXCLUDED.count,
                    last_timestamp = GREATEST(sensor_agg_10min.last_timestamp, EXCLUDED.last_timestamp)";

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            foreach (var entity in entities)
            {
                command.Parameters.Clear();
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@bucket", entity.Bucket));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@factory_id", (int)entity.FactoryId));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@tag", entity.Tag));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@equipment_type", entity.EquipmentType));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@equipment_name", entity.EquipmentName));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@source_id", entity.SourceId));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@avg_value", entity.AvgValue));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@min_value", entity.MinValue));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@max_value", entity.MaxValue));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@count", entity.Count));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@last_timestamp", entity.LastTimestamp));

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Flushed {Count} 10-minute aggregations", entities.Count);
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private async Task Flush1HourAggregationsAsync(
        GatewayDbContext dbContext,
        Dictionary<string, AggregationStats> stats,
        CancellationToken cancellationToken)
    {
        var entities = stats.Values.Select(s => new SensorAggregation1HourEntity
        {
            Bucket = s.Bucket,
            FactoryId = s.FactoryId,
            Tag = s.Tag,
            EquipmentType = s.EquipmentType,
            EquipmentName = s.EquipmentName,
            SourceId = s.SourceId,
            AvgValue = s.Sum / s.Count,
            MinValue = s.Min,
            MaxValue = s.Max,
            Count = s.Count
        }).ToList();

        if (entities.Count == 0)
        {
            return;
        }

        // Use raw SQL for UPSERT (ON CONFLICT DO UPDATE)
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Batch UPSERT using PostgreSQL ON CONFLICT
            // Recalculate average: (old_avg * old_count + new_avg * new_count) / (old_count + new_count)
            var sql = @"
                INSERT INTO sensor_agg_1hour (bucket, factory_id, tag, equipment_type, equipment_name, source_id, avg_value, min_value, max_value, count)
                VALUES (@bucket, @factory_id, @tag, @equipment_type, @equipment_name, @source_id, @avg_value, @min_value, @max_value, @count)
                ON CONFLICT (bucket, factory_id, tag, equipment_type, equipment_name, source_id)
                DO UPDATE SET
                    avg_value = (sensor_agg_1hour.avg_value * sensor_agg_1hour.count + EXCLUDED.avg_value * EXCLUDED.count) / (sensor_agg_1hour.count + EXCLUDED.count),
                    min_value = LEAST(sensor_agg_1hour.min_value, EXCLUDED.min_value),
                    max_value = GREATEST(sensor_agg_1hour.max_value, EXCLUDED.max_value),
                    count = sensor_agg_1hour.count + EXCLUDED.count";

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            foreach (var entity in entities)
            {
                command.Parameters.Clear();
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@bucket", entity.Bucket));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@factory_id", (int)entity.FactoryId));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@tag", entity.Tag));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@equipment_type", entity.EquipmentType));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@equipment_name", entity.EquipmentName));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@source_id", entity.SourceId));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@avg_value", entity.AvgValue));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@min_value", entity.MinValue));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@max_value", entity.MaxValue));
                command.Parameters.Add(new Npgsql.NpgsqlParameter("@count", entity.Count));

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Flushed {Count} 1-hour aggregations", entities.Count);
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private static decimal? ExtractNumericValue(JsonElement valueElement)
    {
        try
        {
            // Try to get "value" property from JSON object
            if (valueElement.ValueKind == JsonValueKind.Object && valueElement.TryGetProperty("value", out var valueProp))
            {
                if (valueProp.ValueKind == JsonValueKind.Number)
                {
                    return valueProp.GetDecimal();
                }
            }
            // If it's a number directly
            else if (valueElement.ValueKind == JsonValueKind.Number)
            {
                return valueElement.GetDecimal();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static DateTime Calculate10MinBucket(DateTime timestamp)
    {
        // Floor to 10-minute boundary
        var minutes = timestamp.Minute;
        var flooredMinutes = (minutes / 10) * 10;
        return new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, flooredMinutes, 0, DateTimeKind.Utc);
    }

    private static DateTime Calculate1HourBucket(DateTime timestamp)
    {
        // Floor to hour boundary
        return new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, 0, 0, DateTimeKind.Utc);
    }

    private static string CreateKey(DateTime bucket, TelemetryEvent evt)
    {
        return $"{bucket:yyyy-MM-dd HH:mm:ss}|{(int)evt.FactoryId}|{evt.Tag}|{evt.EquipmentType}|{evt.EquipmentName}|{evt.SourceId}";
    }

    private sealed class AggregationStats
    {
        public DateTime Bucket { get; set; }
        public Factory FactoryId { get; set; }
        public string Tag { get; set; } = string.Empty;
        public string EquipmentType { get; set; } = string.Empty;
        public string EquipmentName { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public decimal Sum { get; set; }
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public long Count { get; set; }
        public DateTime LastTimestamp { get; set; }
    }

    public override void Dispose()
    {
        _flushTimer?.Dispose();
        _flushLock.Dispose();
        base.Dispose();
    }
}

