using Gateway.Adapters.FakeAdapter;
using Gateway.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Gateway.Api.Services;

/// <summary>
/// Handles data from adapters and feeds into the pipeline
/// </summary>
public sealed class AdapterDataHandler : IAdapterDataHandler
{
    private readonly IIngest _ingest;
    private readonly ILogger<AdapterDataHandler> _logger;

    public AdapterDataHandler(
        IIngest ingest,
        ILogger<AdapterDataHandler> logger)
    {
        _ingest = ingest;
        _logger = logger;
    }

    public async Task HandleDataAsync(
        string adapterId,
        string sourceId,
        DateTime timestamp,
        Dictionary<string, object> payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rawData = new RawData
            {
                AdapterId = adapterId,
                SourceId = sourceId,
                Timestamp = timestamp,
                Payload = payload,
                Metadata = new Dictionary<string, string>()
            };

            await _ingest.InputChannel.Writer.WriteAsync(rawData, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling data from adapter {AdapterId}", adapterId);
            throw;
        }
    }
}

