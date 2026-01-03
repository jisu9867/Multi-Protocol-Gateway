using System.Threading.Channels;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Gateway.Api.Pipeline;

/// <summary>
/// Normalization stage implementation
/// </summary>
public sealed class NormalizeStage : INormalize
{
    private readonly ILogger<NormalizeStage> _logger;
    private readonly Channel<RawData> _inputChannel;
    private readonly Channel<TelemetryEvent> _outputChannel;
    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;

    public NormalizeStage(
        ILogger<NormalizeStage> logger,
        BoundedChannelOptions? inputChannelOptions = null,
        BoundedChannelOptions? outputChannelOptions = null)
    {
        _logger = logger;
        
        var inputOptions = inputChannelOptions ?? new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        
        var outputOptions = outputChannelOptions ?? new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        
        _inputChannel = Channel.CreateBounded<RawData>(inputOptions);
        _outputChannel = Channel.CreateBounded<TelemetryEvent>(outputOptions);
    }

    public Channel<RawData> InputChannel => _inputChannel;
    public Channel<TelemetryEvent> OutputChannel => _outputChannel;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_processingTask != null)
        {
            return Task.CompletedTask;
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessAsync(_cancellationTokenSource.Token);
        
        _logger.LogInformation("Normalize stage started");
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

        _outputChannel.Writer.Complete();
        _cancellationTokenSource?.Dispose();
        _processingTask = null;
        
        _logger.LogInformation("Normalize stage stopped");
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await foreach (var rawData in _inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var telemetryEvent = Normalize(rawData);
                
                await _outputChannel.Writer.WriteAsync(telemetryEvent, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error normalizing raw data from adapter {AdapterId}", rawData.AdapterId);
            }
        }
    }

    private static TelemetryEvent Normalize(RawData rawData)
    {
        var payload = rawData.Payload switch
        {
            Dictionary<string, object?> dict => dict,
            _ => new Dictionary<string, object?> { ["value"] = rawData.Payload }
        };

        return new TelemetryEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceId = rawData.SourceId,
            AdapterId = rawData.AdapterId,
            Timestamp = rawData.Timestamp,
            Payload = payload,
            Metadata = rawData.Metadata
        };
    }
}

