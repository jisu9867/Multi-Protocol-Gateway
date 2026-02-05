using Gateway.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Api.Services;

/// <summary>
/// Service for setting up TimescaleDB continuous aggregates (views) on startup
/// This service always runs to ensure views are created, regardless of seed data settings
/// </summary>
public sealed class ContinuousAggregatesSetupService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContinuousAggregatesSetupService> _logger;
    private static bool _hasRun = false;
    private static readonly object _lock = new();

    public ContinuousAggregatesSetupService(
        IServiceProvider serviceProvider,
        ILogger<ContinuousAggregatesSetupService> logger)
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
                _logger.LogInformation("ContinuousAggregatesSetupService already executed, skipping");
                return;
            }
            _hasRun = true;
        }

        // Wait a bit to ensure database migrations are complete
        await Task.Delay(2000, cancellationToken);

        int maxRetries = 3;
        int retryDelayMs = 5000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Starting ContinuousAggregatesSetupService (attempt {Attempt}/{MaxRetries})...", 
                    attempt, maxRetries);

                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
                
                // Ensure database connection is available
                if (!await dbContext.Database.CanConnectAsync(cancellationToken))
                {
                    _logger.LogWarning("Database connection not available, retrying in {DelayMs}ms...", retryDelayMs);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs, cancellationToken);
                        continue;
                    }
                    throw new InvalidOperationException("Database connection not available after multiple attempts");
                }

                var connection = dbContext.Database.GetDbConnection();
                
                // Close connection if already open to avoid conflicts
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    _logger.LogInformation("Database connection already open, closing and reopening...");
                    await connection.CloseAsync();
                }

                await connection.OpenAsync(cancellationToken);
                _logger.LogInformation("Database connection opened successfully");

                // 1. Ensure hypertable exists
                await EnsureHypertableAsync(connection, cancellationToken);

                // 2. Create continuous aggregates (views)
                await CreateContinuousAggregatesAsync(connection, cancellationToken);

                // Close connection
                await connection.CloseAsync();

                _logger.LogInformation("ContinuousAggregatesSetupService completed successfully");
                return; // Success, exit retry loop
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ContinuousAggregatesSetupService (attempt {Attempt}/{MaxRetries}): {ErrorMessage}", 
                    attempt, maxRetries, ex.Message);
                
                if (attempt < maxRetries)
                {
                    _logger.LogInformation("Retrying in {DelayMs}ms...", retryDelayMs);
                    await Task.Delay(retryDelayMs, cancellationToken);
                }
                else
                {
                    _logger.LogError("ContinuousAggregatesSetupService failed after {MaxRetries} attempts. " +
                        "Views may not be created. Please check database connection and permissions.", maxRetries);
                    // Don't throw - allow app to start even if setup fails
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task EnsureHypertableAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking if hypertable exists...");

        try
        {
            // First check if telemetry_events table exists
            var checkTableSql = @"
                SELECT EXISTS (
                    SELECT FROM information_schema.tables 
                    WHERE table_schema = 'public' 
                    AND table_name = 'telemetry_events'
                )";

            using var checkTableCommand = connection.CreateCommand();
            checkTableCommand.CommandText = checkTableSql;
            var tableExists = Convert.ToBoolean(await checkTableCommand.ExecuteScalarAsync(cancellationToken));

            if (!tableExists)
            {
                _logger.LogWarning("telemetry_events table does not exist yet. Hypertable creation will be skipped. " +
                    "Please ensure database migrations are applied first.");
                return;
            }

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring hypertable exists: {ErrorMessage}", ex.Message);
            throw; // Re-throw to trigger retry logic
        }
    }

    private async Task CreateContinuousAggregatesAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating continuous aggregates...");

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating continuous aggregates: {ErrorMessage}", ex.Message);
            _logger.LogError(ex, "Stack trace: {StackTrace}", ex.StackTrace);
            throw; // Re-throw to trigger retry logic
        }
    }
}

