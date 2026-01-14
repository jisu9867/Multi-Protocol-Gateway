using Gateway.Core.Models;
using Gateway.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Api.Controllers;

/// <summary>
/// Controller for telemetry events API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly GatewayDbContext _dbContext;
    private readonly ILogger<EventsController> _logger;

    public EventsController(
        GatewayDbContext dbContext,
        ILogger<EventsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Get recent telemetry events
    /// </summary>
    /// <param name="factoryId">Optional factory ID filter</param>
    /// <param name="equipmentType">Optional equipment type filter</param>
    /// <param name="equipmentName">Optional equipment name filter</param>
    /// <param name="limit">Maximum number of events to return (default: 50, max: 1000)</param>
    /// <returns>List of recent telemetry events</returns>
    [HttpGet("recent")]
    public async Task<ActionResult<IEnumerable<object>>> GetRecentEvents(
        [FromQuery] Factory? factoryId = null,
        [FromQuery] string? equipmentType = null,
        [FromQuery] string? equipmentName = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || limit > 1000)
        {
            return BadRequest(new { error = "Limit must be between 1 and 1000" });
        }

        try
        {
            var query = _dbContext.TelemetryEvents.AsQueryable();

            // Apply filters
            if (factoryId.HasValue)
            {
                query = query.Where(e => e.FactoryId == factoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(equipmentType))
            {
                query = query.Where(e => e.EquipmentType == equipmentType);
            }

            if (!string.IsNullOrWhiteSpace(equipmentName))
            {
                query = query.Where(e => e.EquipmentName == equipmentName);
            }

            var events = await query
                .OrderByDescending(e => e.Timestamp)
                .Take(limit)
                .Select(e => new
                {
                    e.EventId,
                    e.Timestamp,
                    e.FactoryId,
                    e.SourceId,
                    e.EquipmentType,
                    e.EquipmentName,
                    e.Tag,
                    e.Sequence,
                    e.Quality,
                    Value = e.ValueJson,
                    e.RouteKey,
                    e.TraceId
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recent events");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

