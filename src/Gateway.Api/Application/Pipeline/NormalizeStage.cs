using System.Text.Json;
using System.Threading.Channels;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Gateway.Application.Pipeline;

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

        _logger.LogDebug("Normalize stage started");
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

        _logger.LogDebug("Normalize stage stopped");
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
        JsonElement value;
        if (rawData.Payload is Dictionary<string, object?> dict)
        {
            value = JsonSerializer.SerializeToElement(dict);
        }
        else
        {
            value = JsonSerializer.SerializeToElement(new { value = rawData.Payload });
        }

        Factory factoryId = Factory.Ulsan;
        if (rawData.Metadata.TryGetValue("factory_id", out var factoryIdValue))
        {
            if (Enum.TryParse<Factory>(factoryIdValue, ignoreCase: true, out var parsedFactory))
            {
                if (parsedFactory == Factory.Ulsan || parsedFactory == Factory.Asan || parsedFactory == Factory.Jeonju)
                {
                    factoryId = parsedFactory;
                }
                else
                {
                    factoryId = Factory.Ulsan;
                }
            }
            else if (int.TryParse(factoryIdValue, out var factoryInt) && Enum.IsDefined(typeof(Factory), factoryInt))
            {
                var parsedFactoryFromInt = (Factory)factoryInt;
                if (parsedFactoryFromInt == Factory.Ulsan || parsedFactoryFromInt == Factory.Asan || parsedFactoryFromInt == Factory.Jeonju)
                {
                    factoryId = parsedFactoryFromInt;
                }
                else
                {
                    factoryId = Factory.Ulsan;
                }
            }
        }

        var equipmentType = rawData.Metadata.TryGetValue("equipment_type", out var equipmentTypeValue)
            ? equipmentTypeValue
            : "unknown";

        var equipmentName = rawData.Metadata.TryGetValue("equipment_name", out var equipmentNameValue)
            ? equipmentNameValue
            : rawData.SourceId;

        var tag = rawData.Metadata.TryGetValue("tag", out var tagValue)
            ? tagValue
            : "default";

        var sequence = rawData.Metadata.TryGetValue("sequence", out var seqValue)
            && long.TryParse(seqValue, out var seq)
            ? seq
            : 0L;

        var traceId = rawData.Metadata.TryGetValue("traceId", out var traceValue)
            ? traceValue
            : null;

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
            utcTimestamp = DateTime.SpecifyKind(rawData.Timestamp, DateTimeKind.Utc);
        }

        return new TelemetryEvent
        {
            EventId = Guid.NewGuid(),
            FactoryId = factoryId,
            SourceId = rawData.SourceId,
            EquipmentType = equipmentType,
            EquipmentName = equipmentName,
            Tag = tag,
            Value = value,
            Timestamp = new DateTimeOffset(utcTimestamp, TimeSpan.Zero),
            Quality = DataQuality.Good,
            Sequence = sequence,
            TraceId = traceId
        };
    }
}
