using System.Threading.Channels;
using Gateway.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Gateway.Application.Pipeline;

/// <summary>
/// Ingestion stage implementation
/// </summary>
public sealed class IngestStage : IIngest
{
    private readonly ILogger<IngestStage> _logger;
    private readonly Channel<RawData> _inputChannel;

    public IngestStage(
        ILogger<IngestStage> logger,
        BoundedChannelOptions? channelOptions = null)
    {
        _logger = logger;

        var options = channelOptions ?? new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };

        _inputChannel = Channel.CreateBounded<RawData>(options);
    }

    public Channel<RawData> InputChannel => _inputChannel;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Ingest stage started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _inputChannel.Writer.Complete();
        _logger.LogDebug("Ingest stage stopped");
        return Task.CompletedTask;
    }
}
