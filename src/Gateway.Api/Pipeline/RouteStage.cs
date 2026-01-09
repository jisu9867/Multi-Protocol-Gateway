using System.Threading.Channels;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Gateway.Api.Pipeline;

/// <summary>
/// Routing stage implementation - routes to all sinks based on tag prefix
/// </summary>
public sealed class RouteStage : IRoute
{
    private readonly ILogger<RouteStage> _logger;
    private readonly Channel<TelemetryEvent> _inputChannel;
    private readonly List<ISink> _sinks;
    private readonly List<Channel<TelemetryEvent>> _outputChannels;
    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;

    public RouteStage(
        ILogger<RouteStage> logger,
        IEnumerable<ISink> sinks,
        BoundedChannelOptions? inputChannelOptions = null)
    {
        _logger = logger;
        _sinks = sinks.ToList();
        
        var inputOptions = inputChannelOptions ?? new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        
        _inputChannel = Channel.CreateBounded<TelemetryEvent>(inputOptions);
        
        // Use sink input channels as output channels
        _outputChannels = _sinks.Select(sink => sink.InputChannel).ToList();
    }

    public Channel<TelemetryEvent> InputChannel => _inputChannel;
    public IReadOnlyList<Channel<TelemetryEvent>> OutputChannels => _outputChannels;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_processingTask != null)
        {
            return Task.CompletedTask;
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessAsync(_cancellationTokenSource.Token);
        
        _logger.LogInformation("Route stage started with {SinkCount} sinks", _sinks.Count);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_processingTask == null)
        {
            return;
        }

        _inputChannel.Writer.Complete();
        
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
        }

        try
        {
            await _processingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _cancellationTokenSource?.Dispose();
        _processingTask = null;
        
        _logger.LogInformation("Route stage stopped");
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await foreach (var telemetryEvent in _inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                // Simple routing: route to all sinks (tag prefix routing can be added here if needed)
                // For now, all events go to all sinks
                var tasks = _outputChannels.Select(async channel =>
                {
                    try
                    {
                        var canWrite = await channel.Writer.WaitToWriteAsync(cancellationToken)
                            .ConfigureAwait(false);
                        
                        if (canWrite)
                        {
                            await channel.Writer.WriteAsync(telemetryEvent, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown - ignore silently
                    }
                    catch (TaskCanceledException)
                    {
                        // Expected during shutdown - ignore silently
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error routing event {EventId} to sink", telemetryEvent.EventId);
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error routing telemetry event {EventId}", telemetryEvent.EventId);
            }
        }
    }
}
