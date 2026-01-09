using System.Text.Json;
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
            catch (OperationCanceledException)
            {
                // Expected during shutdown - ignore silently
                break;
            }
            catch (TaskCanceledException)
            {
                // Expected during shutdown - ignore silently
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error normalizing raw data from adapter {AdapterId}", rawData.AdapterId);
            }
        }
    }

    private static TelemetryEvent Normalize(RawData rawData)
    {
        // Convert payload to JsonElement
        JsonElement value;
        if (rawData.Payload is Dictionary<string, object?> dict)
        {
            value = JsonSerializer.SerializeToElement(dict);
        }
        else
        {
            value = JsonSerializer.SerializeToElement(new { value = rawData.Payload });
        }

        // Extract tag from metadata or use default
        var tag = rawData.Metadata.TryGetValue("tag", out var tagValue) 
            ? tagValue 
            : "default";

        // Extract sequence from metadata or use 0
        var sequence = rawData.Metadata.TryGetValue("sequence", out var seqValue) 
            && long.TryParse(seqValue, out var seq)
            ? seq 
            : 0L;

        // Extract traceId from metadata if available
        var traceId = rawData.Metadata.TryGetValue("traceId", out var traceValue) 
            ? traceValue 
            : null;

        // Ensure timestamp is UTC
        DateTime utcTimestamp;
        if (rawData.Timestamp.Kind == DateTimeKind.Utc)
        {
            utcTimestamp = rawData.Timestamp;
        }
        else if (rawData.Timestamp.Kind == DateTimeKind.Local)
        {
            utcTimestamp = rawData.Timestamp.ToUniversalTime();
        }
        else
        {
            // Unspecified - assume UTC
            utcTimestamp = DateTime.SpecifyKind(rawData.Timestamp, DateTimeKind.Utc);
        }

        return new TelemetryEvent
        {
            EventId = Guid.NewGuid(),
            SourceId = rawData.SourceId,
            Tag = tag,
            Value = value,
            Timestamp = new DateTimeOffset(utcTimestamp, TimeSpan.Zero),
            Quality = DataQuality.Good,
            Sequence = sequence,
            TraceId = traceId
        };
    }
}

