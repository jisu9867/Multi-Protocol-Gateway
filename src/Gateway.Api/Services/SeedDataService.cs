using System.Text.Json;
using Gateway.Core.Models;
using Gateway.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Gateway.Api.Services;

/// <summary>
/// Seed service for populating sample telemetry data and aggregation tables
/// Controlled by ENABLE_SEED_DATA environment variable or Gateway:EnableSeedData configuration
/// </summary>
public sealed class SeedDataService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SeedDataService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private static bool _hasRun = false;
    private static readonly object _lock = new();

    public SeedDataService(
        IServiceProvider serviceProvider,
        ILogger<SeedDataService> logger,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Check if seed data is enabled
        var enableSeedData = _configuration.GetValue<bool>("Gateway:EnableSeedData", false) ||
                            _configuration.GetValue<bool>("ENABLE_SEED_DATA", false) ||
                            bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_SEED_DATA"), out var envValue) && envValue;
        
        if (!enableSeedData)
        {
            _logger.LogInformation("SeedDataService skipped (seed data not enabled). Environment: {Environment}", 
                _environment.EnvironmentName);
            return;
        }
        
        _logger.LogInformation("SeedDataService enabled. Environment: {Environment}", 
            _environment.EnvironmentName);

        // Ensure this only runs once
        lock (_lock)
        {
            if (_hasRun)
            {
                _logger.LogInformation("SeedDataService already executed, skipping");
                return;
            }
            _hasRun = true;
        }

        try
        {
            _logger.LogInformation("Starting SeedDataService...");

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

            // 1. Seed sample data if table is empty
            var seeded = await SeedSampleDataAsync(dbContext, cancellationToken);

            // 2. Populate aggregation tables from existing telemetry_events data
            // Always populate aggregation tables, even if data already existed
            await PopulateAggregationTablesAsync(dbContext, cancellationToken);

            _logger.LogInformation("SeedDataService completed successfully: Data seeded ✓, Aggregation tables populated ✓");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SeedDataService: {ErrorMessage}", ex.Message);
            _logger.LogError(ex, "Stack trace: {StackTrace}", ex.StackTrace);
            // Don't throw - allow app to start even if seed fails
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task<bool> SeedSampleDataAsync(GatewayDbContext dbContext, CancellationToken cancellationToken)
    {
        // Check if data already exists
        var existingCount = await dbContext.TelemetryEvents.CountAsync(cancellationToken);
        if (existingCount > 0)
        {
            _logger.LogInformation("Telemetry data already exists ({Count} records), skipping seed", existingCount);
            return false;
        }

        _logger.LogInformation("Seeding sample telemetry data...");

        var random = new Random();
        var factories = new[] { Factory.Ulsan, Factory.Asan, Factory.Jeonju };
        var tags = new[] { "temp", "humidity", "pressure", "vibration", "power", "flow" };
        var equipmentTypes = new[] { "Sensor", "Meter", "Monitor" };
        
        // Generate data for the last 24 hours, 10-minute intervals
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
        return true;
    }

    private async Task PopulateAggregationTablesAsync(GatewayDbContext dbContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Populating aggregation tables from telemetry_events data...");

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

