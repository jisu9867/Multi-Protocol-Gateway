using Gateway.Adapters.FakeAdapter;
using Gateway.Adapters.MqttAdapter;
using Gateway.Api.Configuration;
using Gateway.Api.Services;
using Gateway.Api.HealthChecks;
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
        policy.WithOrigins("http://localhost:5270", "https://localhost:7003")
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

// Kafka Consumer (reads from Kafka and forwards to PostgreSQL)
builder.Services.AddSingleton<KafkaConsumer>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<KafkaConsumer>>();
    var kafkaOptions = sp.GetRequiredService<IOptions<KafkaOptions>>();
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

var app = builder.Build();

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

// Ensure database migrations are applied
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Applying database migrations...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations.");
        // Don't throw - allow app to start even if migration fails
        // Migration errors will be visible in health checks
    }
}

app.Run();

public partial class Program { }
