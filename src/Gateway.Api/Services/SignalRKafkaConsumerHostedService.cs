using Gateway.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Api.Services;

/// <summary>
/// Wrapper Hosted Service to explicitly start the SignalR Kafka Consumer
/// Since the consumer is registered as a KeyedService singleton, we need to manually start it
/// </summary>
public class SignalRKafkaConsumerHostedService : BackgroundService
{
    private readonly ILogger<SignalRKafkaConsumerHostedService> _logger;
    private readonly KafkaConsumer _consumer;

    public SignalRKafkaConsumerHostedService(
        ILogger<SignalRKafkaConsumerHostedService> logger,
        [FromKeyedServices("SignalR")] KafkaConsumer consumer)
    {
        _logger = logger;
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        
        _logger.LogInformation("SignalRKafkaConsumerHostedService created. Consumer type: {Type}", consumer.GetType().Name);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SignalRKafkaConsumerHostedService: Starting SignalR Kafka Consumer...");
        
        // Since KafkaConsumer is a BackgroundService registered as KeyedService,
        // we need to manually call its ExecuteAsync method using reflection
        // BackgroundService.StartAsync would create a new task, but we want to run it in this context
        try
        {
            _logger.LogInformation("SignalRKafkaConsumerHostedService: Calling consumer.ExecuteAsync via reflection...");
            
            // Get the protected ExecuteAsync method using reflection
            var executeAsyncMethod = typeof(KafkaConsumer).GetMethod("ExecuteAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (executeAsyncMethod != null)
            {
                _logger.LogInformation("SignalRKafkaConsumerHostedService: Found ExecuteAsync method, invoking...");
                var task = (Task)executeAsyncMethod.Invoke(_consumer, new object[] { stoppingToken })!;
                await task.ConfigureAwait(false);
                _logger.LogInformation("SignalRKafkaConsumerHostedService: Consumer.ExecuteAsync completed");
            }
            else
            {
                _logger.LogError("SignalRKafkaConsumerHostedService: Failed to find ExecuteAsync method on KafkaConsumer");
                throw new InvalidOperationException("Failed to find ExecuteAsync method on KafkaConsumer");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
            _logger.LogInformation("SignalRKafkaConsumerHostedService: Cancellation requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalRKafkaConsumerHostedService: Error starting SignalR Kafka Consumer");
            throw;
        }
    }
}

