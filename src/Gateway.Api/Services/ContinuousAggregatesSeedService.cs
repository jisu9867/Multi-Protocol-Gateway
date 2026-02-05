using Gateway.Core.Models;
using Gateway.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Gateway.Api.Services;

/// <summary>
/// Seed service for creating TimescaleDB continuous aggregates on startup
/// Controlled by ENABLE_SEED_DATA environment variable or Gateway:EnableSeedData configuration
/// </summary>
public sealed class ContinuousAggregatesSeedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContinuousAggregatesSeedService> _logger;
    private readonly IConfiguration _configuration;
    private static bool _hasRun = false;
    private static readonly object _lock = new();

    public ContinuousAggregatesSeedService(
        IServiceProvider serviceProvider,
        ILogger<ContinuousAggregatesSeedService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Check if seed data is enabled via environment variable or configuration
        var enableSeedData = _configuration.GetValue<bool>("Gateway:EnableSeedData", false) ||
                            _configuration.GetValue<bool>("ENABLE_SEED_DATA", false);
        
        if (!enableSeedData)
        {
            _logger.LogInformation("ContinuousAggregatesSeedService skipped (ENABLE_SEED_DATA is not enabled)");
            return;
        }

        // Ensure this only runs once
        lock (_lock)
        {
            if (_hasRun)
            {
                _logger.LogInformation("ContinuousAggregatesSeedService already executed, skipping");
                return;
            }
            _hasRun = true;
        }

        try
        {
            _logger.LogInformation("Starting ContinuousAggregatesSeedService...");

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            var connection = dbContext.Database.GetDbConnection();

            await connection.OpenAsync(cancellationToken);

            // 1. Ensure hypertable exists
            await EnsureHypertableAsync(connection, cancellationToken);

            // 2. Seed sample data if table is empty
            await SeedSampleDataAsync(dbContext, cancellationToken);

            // 3. Create continuous aggregates
            await CreateContinuousAggregatesAsync(connection, cancellationToken);

            // 4. Refresh initial data
            await RefreshContinuousAggregatesAsync(connection, cancellationToken);

            _logger.LogInformation("ContinuousAggregatesSeedService completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ContinuousAggregatesSeedService");
            // Don't throw - allow app to start even if seed fails
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task EnsureHypertableAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking if hypertable exists...");

        var checkHypertableSql = @"
            SELECT COUNT(*) 
            FROM timescaledb_information.hypertables 
            WHERE hypertable_name = 'telemetry_events'";

        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = checkHypertableSql;
        var exists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync(cancellationToken)) > 0;

        if (!exists)
        {
            _logger.LogInformation("Creating hypertable for telemetry_events...");

            // Check if table has data
            var checkDataSql = "SELECT COUNT(*) FROM telemetry_events";
            using var dataCommand = connection.CreateCommand();
            dataCommand.CommandText = checkDataSql;
            var hasData = Convert.ToInt32(await dataCommand.ExecuteScalarAsync(cancellationToken)) > 0;

            var createHypertableSql = hasData
                ? "SELECT create_hypertable('telemetry_events', 'timestamp', migrate_data => TRUE)"
                : "SELECT create_hypertable('telemetry_events', 'timestamp', if_not_exists => TRUE)";

            using var createCommand = connection.CreateCommand();
            createCommand.CommandText = createHypertableSql;
            await createCommand.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Hypertable created successfully");
        }
        else
        {
            _logger.LogInformation("Hypertable already exists");
        }
    }

    private async Task CreateContinuousAggregatesAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating continuous aggregates...");

        // Check if 10-minute aggregate exists
        var check10MinSql = @"
            SELECT COUNT(*) 
            FROM timescaledb_information.continuous_aggregates 
            WHERE view_name = 'sensor_readings_10min'";

        using var check10MinCommand = connection.CreateCommand();
        check10MinCommand.CommandText = check10MinSql;
        var exists10Min = Convert.ToInt32(await check10MinCommand.ExecuteScalarAsync(cancellationToken)) > 0;

        if (!exists10Min)
        {
            _logger.LogInformation("Creating sensor_readings_10min continuous aggregate...");

            var create10MinSql = @"
                CREATE MATERIALIZED VIEW sensor_readings_10min
                WITH (timescaledb.continuous) AS
                SELECT 
                    time_bucket('10 minutes', timestamp) AS bucket,
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
                WITH NO DATA";

            using var create10MinCommand = connection.CreateCommand();
            create10MinCommand.CommandText = create10MinSql;
            await create10MinCommand.ExecuteNonQueryAsync(cancellationToken);

            // Add refresh policy
            var policy10MinSql = @"
                SELECT add_continuous_aggregate_policy('sensor_readings_10min',
                    start_offset => INTERVAL '3 hours',
                    end_offset => INTERVAL '10 minutes',
                    schedule_interval => INTERVAL '5 minutes',
                    if_not_exists => TRUE)";

            using var policy10MinCommand = connection.CreateCommand();
            policy10MinCommand.CommandText = policy10MinSql;
            await policy10MinCommand.ExecuteNonQueryAsync(cancellationToken);

            // Create indexes
            var index10MinSql = @"
                CREATE INDEX IF NOT EXISTS idx_sensor_readings_10min_factory_tag 
                    ON sensor_readings_10min (factory_id, tag, bucket DESC);
                CREATE INDEX IF NOT EXISTS idx_sensor_readings_10min_last_timestamp 
                    ON sensor_readings_10min (last_timestamp DESC)";

            using var index10MinCommand = connection.CreateCommand();
            index10MinCommand.CommandText = index10MinSql;
            await index10MinCommand.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("sensor_readings_10min continuous aggregate created");
        }
        else
        {
            _logger.LogInformation("sensor_readings_10min continuous aggregate already exists");
        }

        // Check if 1-hour aggregate exists
        var check1HourSql = @"
            SELECT COUNT(*) 
            FROM timescaledb_information.continuous_aggregates 
            WHERE view_name = 'sensor_trends_1hour'";

        using var check1HourCommand = connection.CreateCommand();
        check1HourCommand.CommandText = check1HourSql;
        var exists1Hour = Convert.ToInt32(await check1HourCommand.ExecuteScalarAsync(cancellationToken)) > 0;

        if (!exists1Hour)
        {
            _logger.LogInformation("Creating sensor_trends_1hour continuous aggregate...");

            var create1HourSql = @"
                CREATE MATERIALIZED VIEW sensor_trends_1hour
                WITH (timescaledb.continuous) AS
                SELECT 
                    time_bucket('1 hour', timestamp) AS bucket,
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
                WITH NO DATA";

            using var create1HourCommand = connection.CreateCommand();
            create1HourCommand.CommandText = create1HourSql;
            await create1HourCommand.ExecuteNonQueryAsync(cancellationToken);

            // Add refresh policy
            var policy1HourSql = @"
                SELECT add_continuous_aggregate_policy('sensor_trends_1hour',
                    start_offset => INTERVAL '25 hours',
                    end_offset => INTERVAL '1 hour',
                    schedule_interval => INTERVAL '15 minutes',
                    if_not_exists => TRUE)";

            using var policy1HourCommand = connection.CreateCommand();
            policy1HourCommand.CommandText = policy1HourSql;
            await policy1HourCommand.ExecuteNonQueryAsync(cancellationToken);

            // Create indexes
            var index1HourSql = @"
                CREATE INDEX IF NOT EXISTS idx_sensor_trends_1hour_factory_tag 
                    ON sensor_trends_1hour (factory_id, tag, bucket DESC)";

            using var index1HourCommand = connection.CreateCommand();
            index1HourCommand.CommandText = index1HourSql;
            await index1HourCommand.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("sensor_trends_1hour continuous aggregate created");
        }
        else
        {
            _logger.LogInformation("sensor_trends_1hour continuous aggregate already exists");
        }
    }

    private async Task SeedSampleDataAsync(GatewayDbContext dbContext, CancellationToken cancellationToken)
    {
        // Check if data already exists
        var existingCount = await dbContext.TelemetryEvents.CountAsync(cancellationToken);
        if (existingCount > 0)
        {
            _logger.LogInformation("Telemetry data already exists ({Count} records), skipping seed", existingCount);
            return;
        }

        _logger.LogInformation("Seeding sample telemetry data...");

        var random = new Random();
        var factories = new[] { Factory.Ulsan, Factory.Asan, Factory.Jeonju };
        var tags = new[] { "temp", "humidity", "pressure", "vibration", "power", "flow" };
        var equipmentTypes = new[] { "Sensor", "Meter", "Monitor" };
        var equipmentNames = new[] { "Temperature Sensor 1", "Humidity Sensor 1", "Pressure Sensor 1", "Vibration Sensor 1", "Power Meter 1", "Flow Meter 1" };
        
        // Generate data for the last 24 hours, 10-minute intervals for better trend visualization
        var now = DateTime.UtcNow;
        var startTime = now.AddHours(-24);
        var events = new List<TelemetryEventEntity>();

        // All factories have 3 lines
        var lines = new[] { 1, 2, 3 };

        foreach (var factory in factories)
        {
            foreach (var lineNumber in lines)
            {
                // Generate data every 10 minutes for the last 24 hours (144 data points per tag per line)
                for (int interval = 0; interval < 144; interval++)
                {
                    var timestamp = startTime.AddMinutes(interval * 10);
                    var hour = interval / 6; // 6 intervals per hour
                    
                    foreach (var tag in tags)
                    {
                        // source_id format: {factory}-line{number}
                        var sourceId = $"{factory.ToString().ToLower()}-line{lineNumber}";
                        var equipmentType = tag switch
                        {
                            "temp" or "humidity" or "pressure" => "Sensor",
                            "power" => "Meter",
                            "flow" => "Meter",
                            _ => "Sensor"
                        };
                        var equipmentName = tag switch
                        {
                            "temp" => $"Temperature Sensor Line{lineNumber}",
                            "humidity" => $"Humidity Sensor Line{lineNumber}",
                            "pressure" => $"Pressure Sensor Line{lineNumber}",
                            "vibration" => $"Vibration Sensor Line{lineNumber}",
                            "power" => $"Power Meter Line{lineNumber}",
                            "flow" => $"Flow Meter Line{lineNumber}",
                            _ => $"Sensor Line{lineNumber}"
                        };

                    // Generate realistic values with some variation
                    var baseValue = tag switch
                    {
                        "temp" => 45.0 + random.NextDouble() * 20, // 45-65°C
                        "humidity" => 40.0 + random.NextDouble() * 20, // 40-60%
                        "pressure" => 1000.0 + random.NextDouble() * 50, // 1000-1050 hPa
                        "vibration" => 0.3 + random.NextDouble() * 0.4, // 0.3-0.7 mm/s
                        "power" => 220.0 + random.NextDouble() * 60, // 220-280 kW
                        "flow" => 110.0 + random.NextDouble() * 30, // 110-140 L/min
                        _ => 50.0
                    };

                    // Add some time-based variation (sine wave pattern)
                    var timeVariation = Math.Sin((hour / 24.0) * 2 * Math.PI) * 5;
                    // Add more variation for 10-minute intervals
                    var minuteVariation = Math.Sin((interval / 144.0) * 2 * Math.PI * 24) * 2;
                    var value = baseValue + timeVariation + minuteVariation + (random.NextDouble() - 0.5) * 10;

                    var valueJson = JsonSerializer.Serialize(new { value = Math.Round(value, 2) });

                        events.Add(new TelemetryEventEntity
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = timestamp,
                            FactoryId = factory,
                            SourceId = sourceId,
                            EquipmentType = equipmentType,
                            EquipmentName = equipmentName,
                            Tag = tag,
                            Sequence = interval * 100 + (int)factory * 10 + lineNumber,
                            Quality = DataQuality.Good,
                            ValueJson = valueJson,
                            RouteKey = "default",
                            TraceId = null
                        });
                    }
                }
            }
        }

        // Batch insert
        await dbContext.TelemetryEvents.AddRangeAsync(events, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded {Count} sample telemetry events", events.Count);
    }

    private async Task RefreshContinuousAggregatesAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Refreshing continuous aggregates with initial data...");

        try
        {
            // Refresh 10-minute aggregate
            var refresh10MinSql = "CALL refresh_continuous_aggregate('sensor_readings_10min', NULL, NULL)";
            using var refresh10MinCommand = connection.CreateCommand();
            refresh10MinCommand.CommandText = refresh10MinSql;
            await refresh10MinCommand.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("sensor_readings_10min refreshed");

            // Refresh 1-hour aggregate
            var refresh1HourSql = "CALL refresh_continuous_aggregate('sensor_trends_1hour', NULL, NULL)";
            using var refresh1HourCommand = connection.CreateCommand();
            refresh1HourCommand.CommandText = refresh1HourSql;
            await refresh1HourCommand.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("sensor_trends_1hour refreshed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error refreshing continuous aggregates (this is normal if no data exists yet)");
        }
    }
}

