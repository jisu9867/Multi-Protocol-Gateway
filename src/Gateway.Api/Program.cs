using Gateway.Adapters.FakeAdapter;
using Gateway.Adapters.MqttAdapter;
using Gateway.Application.Pipeline;
using Gateway.Application.Services;
using Gateway.Api.Configuration;
using Gateway.Api.HealthChecks;
using Gateway.Api.Hubs;
using Gateway.Api.Services;
using Gateway.Core.Adapters;
using Gateway.Core.Observability;
using Gateway.Core.Pipeline;
using Gateway.Infrastructure.Configuration;
using Gateway.Infrastructure.Data;
using Gateway.Infrastructure.Sinks;
using Gateway.Infrastructure.Kafka;
using Gateway.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Logs;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for structured logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Host.UseSerilog();

// OpenTelemetry Configuration
var serviceName = "gateway-api";
var serviceVersion = "1.0.0";

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = builder.Environment.EnvironmentName
    });

// Configure OpenTelemetry Tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: serviceVersion))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.EnrichWithHttpRequest = (activity, request) =>
            {
                activity.SetTag("http.request.method", request.Method);
                activity.SetTag("http.request.path", request.Path);
            };
        })
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
        })
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            options.SetDbStatementForText = true;
            options.EnrichWithIDbCommand = (activity, command) =>
            {
                activity.SetTag("db.statement", command.CommandText);
            };
        })
        .AddSource("Gateway.Adapters.MqttAdapter")
        .AddSource("Gateway.Adapters.FakeAdapter")
        .AddSource("Gateway.Infrastructure.Kafka")
        .AddSource("Gateway.Application.Pipeline")
        .AddSource("Gateway.Application.Services")
        .SetSampler(new TraceIdRatioBasedSampler(1.0)) // Sample 100% in production, adjust as needed
    )
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Http")
        .AddMeter("System.Net.Http")
        // Register custom metric meters
        .AddMeter("Gateway.SignalR")
        .AddMeter("Gateway.Kafka")
        .AddMeter("Gateway.Kafka.Lag")
        .AddMeter("Gateway.Pipeline")
        .AddMeter("Gateway.MQTT")
        .AddPrometheusExporter() // Expose Prometheus metrics endpoint
    );

// Note: Prometheus metrics are exposed via OpenTelemetry Prometheus exporter at /metrics endpoint
// No separate Prometheus metrics server needed

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
            "https://localhost:7003",
            "http://localhost:5001"  // Docker container UI
        };
        
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        allowedOrigins.AddRange(configuredOrigins.Where(origin => !string.IsNullOrWhiteSpace(origin)));

        var originsFromEnv = Environment.GetEnvironmentVariable("CORS__ALLOWED_ORIGINS");
        if (!string.IsNullOrWhiteSpace(originsFromEnv))
        {
            allowedOrigins.AddRange(
                originsFromEnv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(origin => !string.IsNullOrWhiteSpace(origin)));
        }
        
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

// Observability Services
builder.Services.AddSingleton<PipelineMetricsExporter>();
builder.Services.AddSingleton<KafkaLagMetrics>();
builder.Services.AddSingleton<IMqttIngestionMetrics, MqttIngestionMetricsRecorder>();

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
builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<KafkaProducer>());

// Kafka Consumer for PostgreSQL (reads from Kafka and forwards to PostgreSQL)
builder.Services.AddSingleton<KafkaConsumer>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<KafkaConsumer>>();
    var kafkaOptions = sp.GetRequiredService<IOptions<KafkaOptions>>();
    // Use default consumer group ID for PostgreSQL storage
    var consumer = new KafkaConsumer(logger, kafkaOptions);
    
    // Register for lag monitoring
    var lagMetrics = sp.GetRequiredService<KafkaLagMetrics>();
    lagMetrics.RegisterConsumerGroup(kafkaOptions.Value.ConsumerGroupId, kafkaOptions.Value.Topic);
    
    return consumer;
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<KafkaConsumer>());

// PostgreSQL Sink (receives from Kafka Consumer)
builder.Services.AddSingleton<PostgreSqlSink>(sp =>
{
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var logger = sp.GetRequiredService<ILogger<PostgreSqlSink>>();
    var sinkOptions = sp.GetRequiredService<IOptions<SinkOptions>>();
    var pipelineMetrics = sp.GetRequiredService<IPipelineMetrics>();
    return new PostgreSqlSink(scopeFactory, logger, sinkOptions, pipelineMetrics);
});
builder.Services.AddSingleton<ISink>(sp => sp.GetRequiredService<PostgreSqlSink>());

// Service that connects Kafka Consumer to PostgreSQL Sink
builder.Services.AddHostedService<KafkaToPostgreSqlService>();

// Kafka Consumer for Aggregation (separate consumer group for aggregation processing)
builder.Services.AddKeyedSingleton<KafkaConsumer>("Aggregation", (sp, key) =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var kafkaOptions = sp.GetRequiredService<IOptions<KafkaOptions>>();
    var logger = loggerFactory.CreateLogger<KafkaConsumer>();
    var programLogger = sp.GetRequiredService<ILogger<Program>>();
    
    // Use separate consumer group ID for aggregation
    // Use "earliest" offset reset to process all messages (including historical data)
    var aggregationConsumerGroupId = "gateway-aggregator-consumer-group-v1";
    
    programLogger.LogInformation("Creating Aggregation Kafka Consumer with GroupId: {GroupId}, AutoOffsetReset: earliest", aggregationConsumerGroupId);
    logger.LogInformation("Creating Aggregation Kafka Consumer with GroupId: {GroupId}, AutoOffsetReset: earliest", aggregationConsumerGroupId);
    
    var consumer = new KafkaConsumer(logger, kafkaOptions, consumerGroupId: aggregationConsumerGroupId, autoOffsetReset: "earliest");
    
    // Register for lag monitoring
    var lagMetrics = sp.GetRequiredService<KafkaLagMetrics>();
    lagMetrics.RegisterConsumerGroup(aggregationConsumerGroupId, kafkaOptions.Value.Topic);
    
    programLogger.LogInformation("Aggregation Kafka Consumer instance created successfully");
    return consumer;
});

// Aggregation Service (consumes from Aggregation Kafka Consumer and stores aggregated results)
builder.Services.AddHostedService<AggregationService>();

// Aggregation Table Populator Service (populates aggregation tables from existing telemetry_events data)
// Runs once at startup if aggregation tables are empty
builder.Services.AddHostedService<AggregationTablePopulatorService>();

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
    
    // Register for lag monitoring
    var lagMetrics = sp.GetRequiredService<KafkaLagMetrics>();
    lagMetrics.RegisterConsumerGroup(signalRConsumerGroupId, kafkaOptions.Value.Topic);
    
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
        var mqttIngestionMetrics = sp.GetRequiredService<IMqttIngestionMetrics>();
        
        var mqttOptions = new Gateway.Adapters.MqttAdapter.MqttAdapterOptions
        {
            Server = adapterOptions.Mqtt.Server,
            Port = adapterOptions.Mqtt.Port,
            ClientId = adapterOptions.Mqtt.ClientId,
            Username = adapterOptions.Mqtt.Username,
            Password = adapterOptions.Mqtt.Password,
            Topic = adapterOptions.Mqtt.Topic
        };
        
        var adapter = new MqttAdapter("mqtt-001", mqttOptions, logger, dataHandler, mqttIngestionMetrics);
        return adapter;
    });
}

// Pipeline service (hosted service)
builder.Services.AddHostedService<PipelineService>();

// Start observability services
builder.Services.AddHostedService(sp =>
{
    var pipelineMetrics = sp.GetRequiredService<IPipelineMetrics>();
    var logger = sp.GetRequiredService<ILogger<PipelineMetricsExporter>>();
    return new PipelineMetricsExporter(pipelineMetrics, logger);
});

// Seed data service (controlled by ENABLE_SEED_DATA environment variable or Gateway:EnableSeedData configuration)
// When enabled: seeds data into telemetry_events and populates aggregation tables
var enableSeedData = builder.Configuration.GetValue<bool>("Gateway:EnableSeedData", false) ||
                     builder.Configuration.GetValue<bool>("ENABLE_SEED_DATA", false) ||
                     bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_SEED_DATA"), out var seedEnvValue) && seedEnvValue;

if (enableSeedData)
{
    builder.Services.AddHostedService<SeedDataService>();
}


var app = builder.Build();

SignalRMetrics.InitializeMetrics();
KafkaMetrics.InitializeMetrics();
MqttMetrics.InitializeMetrics();
PipelineMetricsExporter.InitializeMetrics();
app.Services.GetRequiredService<KafkaLagMetrics>().SeedMetrics();
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();
appLogger.LogInformation("Gateway API started");

// Configure the HTTP request pipeline

app.UseSerilogRequestLogging();
app.UseCors();
app.UseHttpsRedirection();
app.UseAuthorization();

// Prometheus metrics endpoint (OpenTelemetry Prometheus exporter)
// Map endpoint explicitly so /metrics returns Prometheus text format correctly.
app.MapPrometheusScrapingEndpoint("/metrics");

// Test endpoint to verify metrics (local Development only; not exposed in Docker/Production)
if (app.Environment.IsDevelopment())
{
    app.MapGet("/test-metrics", (ILogger<Program> logger) =>
    {
        try
        {
            logger.LogInformation("Recording test metrics...");
            SignalRMetrics.RecordMessageSent("ulsan", "line1", "temp", TimeSpan.FromMilliseconds(10));
            SignalRMetrics.RecordMessageSent("asan", "line2", "humidity", TimeSpan.FromMilliseconds(15));
            SignalRMetrics.RecordMessageSent("jeonju", "line3", "pressure", TimeSpan.FromMilliseconds(20));
            KafkaMetrics.RecordMessageProcessed("test-group", "test-topic", TimeSpan.FromMilliseconds(5));
            KafkaMetrics.RecordMessageProduced("test-topic", TimeSpan.FromMilliseconds(3));
            MqttMetrics.RecordMessageIngested("test/topic", TimeSpan.FromMilliseconds(8));
            logger.LogInformation("Test metrics recorded successfully");
            return Results.Ok(new { message = "Test metrics recorded. Check /metrics endpoint.", timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recording test metrics");
            return Results.Problem($"Error: {ex.Message}");
        }
    });
}

// Test-only observability endpoint for E2E automation.
// Keep disabled by default and enable explicitly in test environments.
var enableObservabilityTestEndpoints = app.Configuration.GetValue<bool>("Gateway:EnableObservabilityTestEndpoints", false) ||
                                       app.Configuration.GetValue<bool>("ENABLE_OBSERVABILITY_TEST_ENDPOINTS", false) ||
                                       bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_OBSERVABILITY_TEST_ENDPOINTS"), out var testEndpointFromEnv) && testEndpointFromEnv;

if (enableObservabilityTestEndpoints)
{
    app.MapPost("/test/observability/seed", (ILogger<Program> logger, KafkaLagMetrics kafkaLagMetrics) =>
    {
        try
        {
            SignalRMetrics.InitializeMetrics();
            KafkaMetrics.InitializeMetrics();
            MqttMetrics.InitializeMetrics();
            PipelineMetricsExporter.InitializeMetrics();
            kafkaLagMetrics.SeedMetrics();

            logger.LogInformation("Observability metrics seeded for E2E.");
            return Results.Ok(new
            {
                message = "Observability metrics seeded.",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed observability metrics.");
            return Results.Problem($"Error: {ex.Message}");
        }
    });
}

// Health endpoint - using custom HealthController for detailed status
// Built-in health checks are available at /health/ready and /health/live (if configured)

// Legacy metrics endpoint (JSON format) - kept for backward compatibility
// Note: This is at /metrics/json to avoid conflict with OpenTelemetry /metrics endpoint
app.MapGet("/metrics/json", (IPipelineMetrics metrics) =>
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


app.Run();

public partial class Program { }
