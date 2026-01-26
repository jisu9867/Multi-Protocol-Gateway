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
        
        // Manually start the consumer's ExecuteAsync
        // Since the consumer is a BackgroundService but registered as a KeyedService singleton,
        // it won't be automatically started by the HostedService infrastructure
        // We'll use reflection to call the protected ExecuteAsync method
        try
        {
            _logger.LogInformation("SignalRKafkaConsumerHostedService: Calling consumer.ExecuteAsync via reflection...");
            
            // Get the ExecuteAsync method using reflection
            var executeAsyncMethod = typeof(KafkaConsumer).GetMethod("ExecuteAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (executeAsyncMethod != null)
            {
                _logger.LogInformation("SignalRKafkaConsumerHostedService: Found ExecuteAsync method, invoking...");
                var task = (Task)executeAsyncMethod.Invoke(_consumer, new object[] { stoppingToken })!;
                await task;
            }
            else
            {
                _logger.LogError("SignalRKafkaConsumerHostedService: Failed to find ExecuteAsync method on KafkaConsumer");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalRKafkaConsumerHostedService: Error starting SignalR Kafka Consumer");
            throw;
        }
    }
}

