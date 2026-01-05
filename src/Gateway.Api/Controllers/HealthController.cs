using Gateway.Core.Adapters;
using Gateway.Core.Pipeline;
using Gateway.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Api.Controllers;

/// <summary>
/// Enhanced health check controller with adapter status, queue lengths, and last event timestamp
/// </summary>
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IEnumerable<IAdapter> _adapters;
    private readonly IPipelineMetrics _metrics;
    private readonly IIngest _ingest;
    private readonly INormalize _normalize;
    private readonly IRoute _route;
    private readonly IEnumerable<ISink> _sinks;
    private readonly GatewayDbContext _dbContext;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IEnumerable<IAdapter> adapters,
        IPipelineMetrics metrics,
        IIngest ingest,
        INormalize normalize,
        IRoute route,
        IEnumerable<ISink> sinks,
        GatewayDbContext dbContext,
        ILogger<HealthController> logger)
    {
        _adapters = adapters;
        _metrics = metrics;
        _ingest = ingest;
        _normalize = normalize;
        _route = route;
        _sinks = sinks;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Get detailed health status including adapter status, queue lengths, and last event timestamp
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<object>> GetHealth(CancellationToken cancellationToken = default)
    {
        try
        {
            // Get adapter statuses
            var adapterStatuses = await Task.WhenAll(_adapters.Select(async adapter =>
            {
                var health = await adapter.GetHealthAsync(cancellationToken).ConfigureAwait(false);
                return new
                {
                    id = adapter.Id,
                    status = adapter.Status.ToString(),
                    health
                };
            }));

            // Get queue lengths (BoundedChannelReader doesn't expose Count directly, using -1 to indicate unknown)
            var queueLengths = new Dictionary<string, int>
            {
                ["ingest"] = GetChannelCount(_ingest.InputChannel),
                ["normalize"] = GetChannelCount(_normalize.InputChannel),
                ["route"] = GetChannelCount(_route.InputChannel)
            };

            var sinkIndex = 0;
            foreach (var sink in _sinks)
            {
                var sinkName = $"sink_{sinkIndex++}";
                queueLengths[sinkName] = GetChannelCount(sink.InputChannel);
            }

            // Get last event timestamp from database
            DateTimeOffset? lastEventTimestamp = null;
            try
            {
                var lastEvent = await _dbContext.TelemetryEvents
                    .OrderByDescending(e => e.Timestamp)
                    .Select(e => e.Timestamp)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (lastEvent != default)
                {
                    lastEventTimestamp = new DateTimeOffset(lastEvent, TimeSpan.Zero);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error retrieving last event timestamp");
            }

            return Ok(new
            {
                status = "healthy",
                adapters = adapterStatuses,
                queueLengths,
                lastEventTimestamp = lastEventTimestamp?.ToString("O")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting health status");
            return StatusCode(500, new
            {
                status = "unhealthy",
                error = "Internal server error"
            });
        }
    }

    private static int GetChannelCount<T>(System.Threading.Channels.Channel<T> channel)
    {
        // BoundedChannelReader doesn't expose Count property directly
        // Return -1 to indicate unknown (could be enhanced with reflection or custom metrics)
        return -1;
    }
}

