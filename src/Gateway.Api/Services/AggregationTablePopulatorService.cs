using Gateway.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Gateway.Api.Services;

/// <summary>
/// Service that populates aggregation tables from existing telemetry_events data
/// Runs once at startup if aggregation tables are empty
/// </summary>
public sealed class AggregationTablePopulatorService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AggregationTablePopulatorService> _logger;
    private static bool _hasRun = false;
    private static readonly object _lock = new();

    public AggregationTablePopulatorService(
        IServiceProvider serviceProvider,
        ILogger<AggregationTablePopulatorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Ensure this only runs once
        lock (_lock)
        {
            if (_hasRun)
            {
                return;
            }
            _hasRun = true;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

            // Check if aggregation tables are empty
            var count10Min = await dbContext.SensorAggregations10Min.CountAsync(cancellationToken);
            var count1Hour = await dbContext.SensorAggregations1Hour.CountAsync(cancellationToken);

            if (count10Min > 0 && count1Hour > 0)
            {
                _logger.LogInformation("Aggregation tables already have data (10min: {Count10Min}, 1hour: {Count1Hour}), skipping population", 
                    count10Min, count1Hour);
                return;
            }

            // Check if telemetry_events has data
            var eventCount = await dbContext.TelemetryEvents.CountAsync(cancellationToken);
            if (eventCount == 0)
            {
                _logger.LogInformation("No telemetry events found, skipping aggregation table population");
                return;
            }

            _logger.LogInformation("Populating aggregation tables from {EventCount} existing telemetry events...", eventCount);

            await PopulateAggregationTablesAsync(dbContext, cancellationToken);

            _logger.LogInformation("Aggregation tables populated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error populating aggregation tables: {ErrorMessage}", ex.Message);
            // Don't throw - allow app to start even if population fails
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task PopulateAggregationTablesAsync(GatewayDbContext dbContext, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            // Populate 10-minute aggregations
            var sql10Min = @"
                INSERT INTO sensor_agg_10min (bucket, factory_id, tag, equipment_type, equipment_name, source_id, avg_value, min_value, max_value, count, last_timestamp)
                SELECT 
                    date_trunc('hour', timestamp) + (EXTRACT(minute FROM timestamp)::int / 10) * INTERVAL '10 minutes' AS bucket,
                    factory_id,
                    tag,
                    equipment_type,
                    equipment_name,
                    source_id,
                    AVG((value_json::jsonb->>'value')::numeric) AS avg_value,
                    MIN((value_json::jsonb->>'value')::numeric) AS min_value,
                    MAX((value_json::jsonb->>'value')::numeric) AS max_value,
                    COUNT(*) AS count,
                    MAX(timestamp) AS last_timestamp
                FROM telemetry_events
                WHERE quality = 0
                GROUP BY bucket, factory_id, tag, equipment_type, equipment_name, source_id
                ON CONFLICT (bucket, factory_id, tag, equipment_type, equipment_name, source_id)
                DO UPDATE SET
                    avg_value = EXCLUDED.avg_value,
                    min_value = LEAST(sensor_agg_10min.min_value, EXCLUDED.min_value),
                    max_value = GREATEST(sensor_agg_10min.max_value, EXCLUDED.max_value),
                    count = EXCLUDED.count,
                    last_timestamp = GREATEST(sensor_agg_10min.last_timestamp, EXCLUDED.last_timestamp)";

            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql10Min;
                var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Populated {Count} rows in sensor_agg_10min", rowsAffected);
            }

            // Populate 1-hour aggregations
            var sql1Hour = @"
                INSERT INTO sensor_agg_1hour (bucket, factory_id, tag, equipment_type, equipment_name, source_id, avg_value, min_value, max_value, count)
                SELECT 
                    date_trunc('hour', timestamp) AS bucket,
                    factory_id,
                    tag,
                    equipment_type,
                    equipment_name,
                    source_id,
                    AVG((value_json::jsonb->>'value')::numeric) AS avg_value,
                    MIN((value_json::jsonb->>'value')::numeric) AS min_value,
                    MAX((value_json::jsonb->>'value')::numeric) AS max_value,
                    COUNT(*) AS count
                FROM telemetry_events
                WHERE quality = 0
                GROUP BY bucket, factory_id, tag, equipment_type, equipment_name, source_id
                ON CONFLICT (bucket, factory_id, tag, equipment_type, equipment_name, source_id)
                DO UPDATE SET
                    avg_value = EXCLUDED.avg_value,
                    min_value = LEAST(sensor_agg_1hour.min_value, EXCLUDED.min_value),
                    max_value = GREATEST(sensor_agg_1hour.max_value, EXCLUDED.max_value),
                    count = EXCLUDED.count";

            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql1Hour;
                var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Populated {Count} rows in sensor_agg_1hour", rowsAffected);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error populating aggregation tables: {ErrorMessage}", ex.Message);
            throw;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}

