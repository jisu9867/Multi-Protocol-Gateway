using Gateway.Adapters.FakeAdapter;
using Gateway.Adapters.MqttAdapter;
using Gateway.Api.Configuration;
using Gateway.Api.Services;
using Gateway.Api.HealthChecks;
using Gateway.Api.Hubs;
using Gateway.Core.Adapters;
using Gateway.Core.Pipeline;
using Gateway.Infrastructure.Configuration;
using Gateway.Infrastructure.Data;
using Gateway.Infrastructure.Sinks;
using Gateway.Infrastructure.Kafka;
using Gateway.Api.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for structured logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// CORS for Gateway.UI
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = new List<string>
        {
            "http://localhost:5270",
            "https://localhost:7003"
        };
        
        // Add Azure UI URL if configured
        var azureUiUrl = builder.Configuration["Cors:AzureUiUrl"];
        if (!string.IsNullOrWhiteSpace(azureUiUrl))
        {
            allowedOrigins.Add(azureUiUrl);
        }
        
        // Also check environment variable for Azure UI URL (both formats)
        var azureUiUrlFromEnv = Environment.GetEnvironmentVariable("CORS__AZURE_UI_URL");
        if (!string.IsNullOrWhiteSpace(azureUiUrlFromEnv))
        {
            allowedOrigins.Add(azureUiUrlFromEnv);
        }
        
        // Also check Cors__AzureUiUrl format (double underscore)
        var azureUiUrlFromEnv2 = Environment.GetEnvironmentVariable("Cors__AzureUiUrl");
        if (!string.IsNullOrWhiteSpace(azureUiUrlFromEnv2))
        {
            allowedOrigins.Add(azureUiUrlFromEnv2);
        }
        
        // Log allowed origins for debugging
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var corsLogger = loggerFactory.CreateLogger("CORS");
        corsLogger.LogInformation("CORS Allowed Origins: {Origins}", string.Join(", ", allowedOrigins));
        
        policy.WithOrigins(allowedOrigins.ToArray())
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<GatewayDbContext>(options =>
    options.UseNpgsql(dataSource));

// Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql")
    .AddCheck<AdapterHealthCheck>("adapters");

// Pipeline components
builder.Services.AddSingleton<IPipelineMetrics, DefaultPipelineMetrics>();

// Pipeline stages
builder.Services.AddSingleton<IIngest, IngestStage>();
builder.Services.AddSingleton<INormalize, NormalizeStage>();

// Sink options configuration
builder.Services.Configure<SinkOptions>(
    builder.Configuration.GetSection(SinkOptions.SectionName));

// Adapter options configuration
builder.Services.Configure<AdapterOptions>(
    builder.Configuration.GetSection(AdapterOptions.SectionName));

// Kafka options configuration
builder.Services.Configure<KafkaOptions>(
    builder.Configuration.GetSection(KafkaOptions.SectionName));

// Kafka Producer (sends normalized events to Kafka)
builder.Services.AddSingleton<KafkaProducer>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<KafkaProducer>>();
    var kafkaOptions = sp.GetRequiredService<IOptions<KafkaOptions>>();
    return new KafkaProducer(logger, kafkaOptions);
});

// Kafka Consumer for PostgreSQL (reads from Kafka and forwards to PostgreSQL)
builder.Services.AddSingleton<KafkaConsumer>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<KafkaConsumer>>();
    var kafkaOptions = sp.GetRequiredService<IOptions<KafkaOptions>>();
    // Use default consumer group ID for PostgreSQL storage
    return new KafkaConsumer(logger, kafkaOptions);
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<KafkaConsumer>());

// PostgreSQL Sink (receives from Kafka Consumer)
builder.Services.AddSingleton<PostgreSqlSink>(sp =>
{
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var logger = sp.GetRequiredService<ILogger<PostgreSqlSink>>();
    var sinkOptions = sp.GetRequiredService<IOptions<SinkOptions>>();
    return new PostgreSqlSink(scopeFactory, logger, sinkOptions);
});
builder.Services.AddSingleton<ISink>(sp => sp.GetRequiredService<PostgreSqlSink>());

// Service that connects Kafka Consumer to PostgreSQL Sink
builder.Services.AddHostedService<KafkaToPostgreSqlService>();

// Kafka Consumer for SignalR (separate consumer group for real-time streaming)
// Use KeyedService to register a second instance with different consumer group ID
builder.Services.AddKeyedSingleton<KafkaConsumer>("SignalR", (sp, key) =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var kafkaOptions = sp.GetRequiredService<IOptions<KafkaOptions>>();
    var logger = loggerFactory.CreateLogger<KafkaConsumer>();
    var programLogger = sp.GetRequiredService<ILogger<Program>>();
    
    // Use separate consumer group ID for SignalR real-time streaming
    // Use a unique consumer group ID with timestamp to ensure fresh start
    // Use unique consumer group ID for SignalR to ensure fresh start
    // Use "latest" offset reset to only receive new messages (real-time streaming)
    // Change the suffix if you want to reset the consumer group
    var signalRConsumerGroupId = $"gateway-signalr-consumer-group-v2";
    
    programLogger.LogInformation("Creating SignalR Kafka Consumer with GroupId: {GroupId}, AutoOffsetReset: latest", signalRConsumerGroupId);
    logger.LogInformation("Creating SignalR Kafka Consumer with GroupId: {GroupId}, AutoOffsetReset: latest", signalRConsumerGroupId);
    
    var consumer = new KafkaConsumer(logger, kafkaOptions, consumerGroupId: signalRConsumerGroupId, autoOffsetReset: "latest");
    programLogger.LogInformation("SignalR Kafka Consumer instance created successfully");
    return consumer;
});
// Register SignalR Consumer as a hosted service
// Use a wrapper service to ensure the consumer starts properly
// KeyedService instances don't automatically start as HostedServices, so we need a wrapper
builder.Services.AddHostedService<SignalRKafkaConsumerHostedService>();

// SignalR for real-time telemetry streaming
builder.Services.AddSignalR();
builder.Services.AddHostedService<SignalRTelemetryService>();

// JsonlFile Sink (optional, can still receive from RouteStage if needed)
builder.Services.AddSingleton<ISink>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<JsonlFileSink>>();
    var sinkOptions = sp.GetRequiredService<IOptions<SinkOptions>>();
    return new JsonlFileSink(logger, sinkOptions);
});

// Route stage (depends on sinks) - used for JsonlFile sink if needed
builder.Services.AddSingleton<IRoute>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<RouteStage>>();
    var sinks = sp.GetServices<ISink>().ToList();
    return new RouteStage(logger, sinks);
});

// Adapter data handler
builder.Services.AddSingleton<IAdapterDataHandler, AdapterDataHandler>();

// Adapters
var adapterOptions = builder.Configuration.GetSection(AdapterOptions.SectionName).Get<AdapterOptions>() 
    ?? new AdapterOptions();

// FakeAdapter (optional)
if (adapterOptions.EnableFakeAdapter)
{
    builder.Services.AddSingleton<IAdapter>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<FakeAdapter>>();
        var dataHandler = sp.GetRequiredService<IAdapterDataHandler>();
        var adapter = new FakeAdapter("fake-001", logger, dataHandler);
        return adapter;
    });
}

// MQTT Adapter (optional)
if (adapterOptions.Mqtt.Enabled)
{
    builder.Services.AddSingleton<IAdapter>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<MqttAdapter>>();
        var dataHandler = sp.GetRequiredService<IAdapterDataHandler>();
        
        var mqttOptions = new Gateway.Adapters.MqttAdapter.MqttAdapterOptions
        {
            Server = adapterOptions.Mqtt.Server,
            Port = adapterOptions.Mqtt.Port,
            ClientId = adapterOptions.Mqtt.ClientId,
            Username = adapterOptions.Mqtt.Username,
            Password = adapterOptions.Mqtt.Password,
            Topic = adapterOptions.Mqtt.Topic
        };
        
        var adapter = new MqttAdapter("mqtt-001", mqttOptions, logger, dataHandler);
        return adapter;
    });
}

// Pipeline service (hosted service)
builder.Services.AddHostedService<PipelineService>();

// Continuous aggregates seed service (controlled by ENABLE_SEED_DATA environment variable)
// When enabled: seeds data, creates hypertable, creates views, and refreshes views
// Check if seed data is enabled via environment variable or configuration
var enableSeedData = builder.Configuration.GetValue<bool>("Gateway:EnableSeedData", false) ||
                     builder.Configuration.GetValue<bool>("ENABLE_SEED_DATA", false);

if (enableSeedData)
{
    builder.Services.AddHostedService<ContinuousAggregatesSeedService>();
}

var app = builder.Build();

// Log after app is built to confirm all services are registered
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();
appLogger.LogInformation("Application built. All hosted services should be starting now.");

// Configure the HTTP request pipeline

app.UseSerilogRequestLogging();
app.UseCors();
app.UseHttpsRedirection();
app.UseAuthorization();

// Health endpoint - using custom HealthController for detailed status
// Built-in health checks are available at /health/ready and /health/live (if configured)

// Metrics endpoint
app.MapGet("/metrics", (IPipelineMetrics metrics) =>
{
    var snapshot = metrics.GetSnapshot();
    return Results.Ok(new
    {
        ingested = snapshot.IngestedCount,
        normalized = snapshot.NormalizedCount,
        routed = snapshot.RoutedCount,
        persisted = snapshot.PersistedCount,
        dropped = snapshot.DroppedCount,
        averageLatencyMs = snapshot.AverageLatency.TotalMilliseconds,
        queueLengths = snapshot.QueueLengths
    });
});

// Adapter status endpoint
app.MapGet("/adapters", async (IEnumerable<IAdapter> adapters) =>
{
    var statuses = await Task.WhenAll(adapters.Select(async adapter =>
    {
        var health = await adapter.GetHealthAsync();
        return new
        {
            id = adapter.Id,
            status = adapter.Status.ToString(),
            health = health
        };
    }));

    return Results.Ok(statuses);
});

app.MapControllers();

// SignalR Hub endpoint
app.MapHub<TelemetryHub>("/hubs/telemetry");

// Ensure database migrations are applied
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Checking database connection and applying migrations...");
        
        // Check if database can be connected
        var canConnect = await dbContext.Database.CanConnectAsync();
        if (!canConnect)
        {
            logger.LogError("Cannot connect to database. Please check connection string.");
            throw new InvalidOperationException("Cannot connect to database.");
        }
        
        // Check if telemetry_events table exists
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();
        
        var tableExists = false;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT EXISTS (
                    SELECT FROM information_schema.tables 
                    WHERE table_schema = 'public' 
                    AND table_name = 'telemetry_events'
                );";
            var result = await command.ExecuteScalarAsync();
            tableExists = result != null && Convert.ToBoolean(result);
        }
        
        if (!tableExists)
        {
            logger.LogWarning("telemetry_events table does not exist. Checking migration history...");
            
            // Check if migration history exists but table doesn't (inconsistent state)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT EXISTS (
                        SELECT FROM information_schema.tables 
                        WHERE table_schema = 'public' 
                        AND table_name = '__EFMigrationsHistory'
                    );";
                var historyExists = await command.ExecuteScalarAsync();
                if (historyExists != null && Convert.ToBoolean(historyExists))
                {
                    logger.LogWarning("Migration history exists but telemetry_events table is missing. This may indicate the table was dropped manually.");
                    logger.LogInformation("Will attempt to apply migrations. If this fails, you may need to manually clean up __EFMigrationsHistory table.");
                }
            }
        }
        else
        {
            logger.LogInformation("telemetry_events table exists.");
        }
        
        await connection.CloseAsync();
        
        // Apply migrations
        logger.LogInformation("Applying database migrations...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
        
        // Verify table was created
        await connection.OpenAsync();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT EXISTS (
                    SELECT FROM information_schema.tables 
                    WHERE table_schema = 'public' 
                    AND table_name = 'telemetry_events'
                );";
            var result = await command.ExecuteScalarAsync();
            var tableNowExists = result != null && Convert.ToBoolean(result);
            
            if (tableNowExists)
            {
                logger.LogInformation("telemetry_events table verified after migration.");
            }
            else
            {
                logger.LogError("telemetry_events table still does not exist after migration. This is a critical error.");
                throw new InvalidOperationException("telemetry_events table was not created after migration.");
            }
        }
        await connection.CloseAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations. Application will continue but database operations may fail.");
        // Don't throw - allow app to start even if migration fails
        // Migration errors will be visible in health checks
        // However, log critical errors clearly
        if (ex is InvalidOperationException)
        {
            logger.LogCritical("CRITICAL: Database migration failed. The application may not function correctly.");
        }
    }
}

// Ensure hypertable and continuous aggregates (views) are created after migrations
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Ensuring hypertable and continuous aggregates are created...");
        await EnsureHypertableAndViewsAsync(dbContext, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error ensuring hypertable and views: {ErrorMessage}", ex.Message);
        logger.LogError(ex, "Stack trace: {StackTrace}", ex.StackTrace);
        // Don't throw - allow app to start even if view creation fails
    }
}

app.Run();

static async Task EnsureHypertableAndViewsAsync(GatewayDbContext dbContext, Microsoft.Extensions.Logging.ILogger logger)
{
    try
    {
        var connection = dbContext.Database.GetDbConnection();
        
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        
        logger.LogInformation("Executing TimescaleDB setup SQL...");
        
        // TimescaleDB extension 확인 및 생성
        var extensionSql = "CREATE EXTENSION IF NOT EXISTS timescaledb;";
        using (var command = connection.CreateCommand())
        {
            command.CommandText = extensionSql;
            await command.ExecuteNonQueryAsync();
        }
        logger.LogInformation("TimescaleDB extension ensured");
        
        // Hypertable 생성
        var hypertableSql = @"
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM timescaledb_information.hypertables 
                    WHERE hypertable_name = 'telemetry_events'
                ) THEN
                    IF EXISTS (SELECT 1 FROM telemetry_events LIMIT 1) THEN
                        PERFORM create_hypertable('telemetry_events', 'timestamp', migrate_data => TRUE);
                    ELSE
                        PERFORM create_hypertable('telemetry_events', 'timestamp', if_not_exists => TRUE);
                    END IF;
                END IF;
            END $$;";
        
        using (var command = connection.CreateCommand())
        {
            command.CommandText = hypertableSql;
            await command.ExecuteNonQueryAsync();
        }
        logger.LogInformation("Hypertable ensured");
        
        // sensor_readings_10min 뷰 생성
        var view10MinSql = @"
            CREATE MATERIALIZED VIEW IF NOT EXISTS sensor_readings_10min
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
            WITH NO DATA;";
        
        using (var command = connection.CreateCommand())
        {
            command.CommandText = view10MinSql;
            await command.ExecuteNonQueryAsync();
        }
        logger.LogInformation("sensor_readings_10min view created");
        
        // sensor_readings_10min 정책 추가
        var policy10MinSql = @"
            SELECT add_continuous_aggregate_policy('sensor_readings_10min',
                start_offset => INTERVAL '3 hours',
                end_offset => INTERVAL '10 minutes',
                schedule_interval => INTERVAL '5 minutes',
                if_not_exists => TRUE);";
        
        using (var command = connection.CreateCommand())
        {
            command.CommandText = policy10MinSql;
            await command.ExecuteNonQueryAsync();
        }
        logger.LogInformation("sensor_readings_10min policy added");
        
        // sensor_readings_10min 인덱스 생성
        var index10MinSql = @"
            CREATE INDEX IF NOT EXISTS idx_sensor_readings_10min_factory_tag 
                ON sensor_readings_10min (factory_id, tag, bucket DESC);
            CREATE INDEX IF NOT EXISTS idx_sensor_readings_10min_last_timestamp 
                ON sensor_readings_10min (last_timestamp DESC);";
        
        using (var command = connection.CreateCommand())
        {
            command.CommandText = index10MinSql;
            await command.ExecuteNonQueryAsync();
        }
        logger.LogInformation("sensor_readings_10min indexes created");
        
        // sensor_trends_1hour 뷰 생성
        var view1HourSql = @"
            CREATE MATERIALIZED VIEW IF NOT EXISTS sensor_trends_1hour
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
            WITH NO DATA;";
        
        using (var command = connection.CreateCommand())
        {
            command.CommandText = view1HourSql;
            await command.ExecuteNonQueryAsync();
        }
        logger.LogInformation("sensor_trends_1hour view created");
        
        // sensor_trends_1hour 정책 추가
        var policy1HourSql = @"
            SELECT add_continuous_aggregate_policy('sensor_trends_1hour',
                start_offset => INTERVAL '25 hours',
                end_offset => INTERVAL '1 hour',
                schedule_interval => INTERVAL '15 minutes',
                if_not_exists => TRUE);";
        
        using (var command = connection.CreateCommand())
        {
            command.CommandText = policy1HourSql;
            await command.ExecuteNonQueryAsync();
        }
        logger.LogInformation("sensor_trends_1hour policy added");
        
        // sensor_trends_1hour 인덱스 생성
        var index1HourSql = @"
            CREATE INDEX IF NOT EXISTS idx_sensor_trends_1hour_factory_tag 
                ON sensor_trends_1hour (factory_id, tag, bucket DESC);";
        
        using (var command = connection.CreateCommand())
        {
            command.CommandText = index1HourSql;
            await command.ExecuteNonQueryAsync();
        }
        logger.LogInformation("sensor_trends_1hour indexes created");
        
        // 기존 데이터가 있으면 refresh
        var checkDataSql = "SELECT COUNT(*) FROM telemetry_events WHERE quality = 0";
        using (var command = connection.CreateCommand())
        {
            command.CommandText = checkDataSql;
            var dataCount = Convert.ToInt32(await command.ExecuteScalarAsync());
            
            if (dataCount > 0)
            {
                logger.LogInformation("Refreshing views with existing data ({Count} records)...", dataCount);
                
                try
                {
                    var refresh10MinSql = "CALL refresh_continuous_aggregate('sensor_readings_10min', NULL, NULL)";
                    using (var refreshCommand = connection.CreateCommand())
                    {
                        refreshCommand.CommandText = refresh10MinSql;
                        await refreshCommand.ExecuteNonQueryAsync();
                    }
                    logger.LogInformation("sensor_readings_10min refreshed successfully");
                    
                    var refresh1HourSql = "CALL refresh_continuous_aggregate('sensor_trends_1hour', NULL, NULL)";
                    using (var refreshCommand = connection.CreateCommand())
                    {
                        refreshCommand.CommandText = refresh1HourSql;
                        await refreshCommand.ExecuteNonQueryAsync();
                    }
                    logger.LogInformation("sensor_trends_1hour refreshed successfully");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error refreshing views (this may be normal if views were just created): {Message}", ex.Message);
                }
            }
            else
            {
                logger.LogInformation("No existing data found, skipping view refresh");
            }
        }
        
        await connection.CloseAsync();
        logger.LogInformation("Hypertable and continuous aggregates setup completed successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error ensuring hypertable and views: {ErrorMessage}", ex.Message);
        logger.LogError(ex, "Stack trace: {StackTrace}", ex.StackTrace);
        throw;
    }
}

public partial class Program { }
