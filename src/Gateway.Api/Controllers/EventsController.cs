using Gateway.Core.Models;
using Gateway.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Npgsql;

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

    /// <summary>
    /// Get sensor readings aggregated by 10-minute intervals
    /// Returns the latest 10-minute aggregated values for each sensor
    /// </summary>
    /// <param name="factoryId">Factory ID filter (required)</param>
    /// <param name="lineNumber">Optional line number filter (e.g., 1, 2, 3). If provided, filters by source_id containing "-line{number}"</param>
    /// <param name="groupByLine">If true, groups results by line (source_id). If false, returns one sensor per tag.</param>
    /// <returns>List of latest sensor readings (10-minute aggregates)</returns>
    [HttpGet("sensor-readings")]
    public async Task<ActionResult<IEnumerable<object>>> GetSensorReadings(
        [FromQuery] Factory factoryId,
        [FromQuery] int? lineNumber = null,
        [FromQuery] bool groupByLine = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("GetSensorReadings called: FactoryId={FactoryId}, LineNumber={LineNumber}, GroupByLine={GroupByLine}", 
                factoryId, lineNumber, groupByLine);
            
            var connection = _dbContext.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);

            var results = new List<object>();

            // Build WHERE clause with optional line filter
            // Use 24 hours instead of 3 hours to include more data
            var whereClause = "factory_id = @factoryId AND bucket >= NOW() - INTERVAL '24 hours'";
            if (lineNumber.HasValue)
            {
                whereClause += " AND source_id LIKE @linePattern";
            }
            
            _logger.LogInformation("Query WHERE clause: {WhereClause}", whereClause);

            // Build SELECT and ORDER BY based on groupByLine
            string selectClause;
            string orderClause;
            
            if (groupByLine)
            {
                // Group by line (source_id) and tag - returns all lines
                selectClause = @"
                    SELECT DISTINCT ON (source_id, tag)
                        tag,
                        equipment_type,
                        equipment_name,
                        source_id,
                        avg_value,
                        min_value,
                        max_value,
                        count,
                        last_timestamp";
                orderClause = "ORDER BY source_id, tag, last_timestamp DESC";
            }
            else
            {
                // Get one sensor per tag (the most recent one) - default behavior
                selectClause = @"
                    SELECT DISTINCT ON (tag)
                        tag,
                        equipment_type,
                        equipment_name,
                        source_id,
                        avg_value,
                        min_value,
                        max_value,
                        count,
                        last_timestamp";
                orderClause = "ORDER BY tag, last_timestamp DESC";
            }

            var aggregateQuery = $@"
                {selectClause}
                FROM sensor_readings_10min
                WHERE {whereClause}
                {orderClause}";

            using (var command = connection.CreateCommand())
            {
                command.CommandText = aggregateQuery;
                var factoryParam = command.CreateParameter();
                factoryParam.ParameterName = "@factoryId";
                factoryParam.Value = (int)factoryId;
                command.Parameters.Add(factoryParam);

                if (lineNumber.HasValue)
                {
                    var linePatternParam = command.CreateParameter();
                    linePatternParam.ParameterName = "@linePattern";
                    linePatternParam.Value = $"%line{lineNumber.Value}";
                    command.Parameters.Add(linePatternParam);
                }

                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        results.Add(new
                        {
                            Tag = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                            EquipmentType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            EquipmentName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            SourceId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                            Value = reader.IsDBNull(4) ? 0.0 : Convert.ToDouble(reader.GetDecimal(4)),
                            MinValue = reader.IsDBNull(5) ? 0.0 : Convert.ToDouble(reader.GetDecimal(5)),
                            MaxValue = reader.IsDBNull(6) ? 0.0 : Convert.ToDouble(reader.GetDecimal(6)),
                            Count = reader.IsDBNull(7) ? 0L : reader.GetInt64(7),
                            LastUpdated = reader.IsDBNull(8) ? DateTime.UtcNow : reader.GetDateTime(8)
                        });
                    }
                }
            }
            
            _logger.LogInformation("Found {Count} results from continuous aggregate for FactoryId={FactoryId}, LineNumber={LineNumber}", 
                results.Count, factoryId, lineNumber);

            // If no data from continuous aggregate, fallback to direct query from source table
            if (results.Count == 0)
            {
                _logger.LogWarning("No data found in continuous aggregate, falling back to direct query");
                
                // Build WHERE clause for fallback query
                // Use 24 hours instead of 3 hours to include more data
                var fallbackWhereClause = "factory_id = @factoryId AND timestamp >= NOW() - INTERVAL '24 hours' AND quality = 0";
                if (lineNumber.HasValue)
                {
                    fallbackWhereClause += " AND source_id LIKE @linePattern";
                }
                
                _logger.LogInformation("Fallback query WHERE clause: {WhereClause}", fallbackWhereClause);

                string fallbackSelectClause;
                string fallbackOrderClause;
                
                if (groupByLine)
                {
                    fallbackSelectClause = "SELECT DISTINCT ON (source_id, tag)";
                    fallbackOrderClause = "ORDER BY source_id, tag, last_timestamp DESC";
                }
                else
                {
                    fallbackSelectClause = "SELECT DISTINCT ON (tag)";
                    fallbackOrderClause = "ORDER BY tag, last_timestamp DESC";
                }

                var fallbackQuery = $@"
                    WITH aggregated_data AS (
                        SELECT 
                            tag,
                            equipment_type,
                            equipment_name,
                            source_id,
                            AVG((value_json::jsonb->>'value')::numeric) as avg_value,
                            MIN((value_json::jsonb->>'value')::numeric) as min_value,
                            MAX((value_json::jsonb->>'value')::numeric) as max_value,
                            COUNT(*) as count,
                            MAX(timestamp) as last_timestamp
                        FROM telemetry_events
                        WHERE {fallbackWhereClause}
                        GROUP BY tag, equipment_type, equipment_name, source_id
                    )
                    {fallbackSelectClause}
                        tag,
                        equipment_type,
                        equipment_name,
                        source_id,
                        avg_value,
                        min_value,
                        max_value,
                        count,
                        last_timestamp
                    FROM aggregated_data
                    {fallbackOrderClause}";

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = fallbackQuery;
                    var factoryParam = command.CreateParameter();
                    factoryParam.ParameterName = "@factoryId";
                    factoryParam.Value = (int)factoryId;
                    command.Parameters.Add(factoryParam);

                    if (lineNumber.HasValue)
                    {
                        var linePatternParam = command.CreateParameter();
                        linePatternParam.ParameterName = "@linePattern";
                        linePatternParam.Value = $"%line{lineNumber.Value}";
                        command.Parameters.Add(linePatternParam);
                    }

                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            results.Add(new
                            {
                                Tag = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                                EquipmentType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                EquipmentName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                SourceId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                Value = reader.IsDBNull(4) ? 0.0 : Convert.ToDouble(reader.GetDecimal(4)),
                                MinValue = reader.IsDBNull(5) ? 0.0 : Convert.ToDouble(reader.GetDecimal(5)),
                                MaxValue = reader.IsDBNull(6) ? 0.0 : Convert.ToDouble(reader.GetDecimal(6)),
                                Count = reader.IsDBNull(7) ? 0L : reader.GetInt64(7),
                                LastUpdated = reader.IsDBNull(8) ? DateTime.UtcNow : reader.GetDateTime(8)
                            });
                        }
                    }
                }
                
                _logger.LogInformation("Found {Count} results from fallback query", results.Count);
            }

            _logger.LogInformation("Returning {Count} total results for FactoryId={FactoryId}, LineNumber={LineNumber}", 
                results.Count, factoryId, lineNumber);
            
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sensor readings");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get sensor trends aggregated by 1-hour intervals for the last 24 hours
    /// </summary>
    /// <param name="factoryId">Factory ID filter (required)</param>
    /// <param name="tag">Sensor tag filter (optional, e.g., "temp", "humidity", "power")</param>
    /// <param name="lineNumber">Optional line number filter (e.g., 1, 2, 3). If provided, filters by source_id containing "-line{number}"</param>
    /// <returns>List of hourly aggregated sensor values for the last 24 hours</returns>
    [HttpGet("sensor-trends")]
    public async Task<ActionResult<IEnumerable<object>>> GetSensorTrends(
        [FromQuery] Factory factoryId,
        [FromQuery] string? tag = null,
        [FromQuery] int? lineNumber = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Build WHERE clause with optional filters
            var whereClause = "factory_id = @factoryId AND bucket >= NOW() - INTERVAL '24 hours' AND bucket < NOW()";
            if (tag != null)
            {
                whereClause += " AND tag = @tag";
            }
            if (lineNumber.HasValue)
            {
                whereClause += " AND source_id LIKE @linePattern";
            }

            // Query the continuous aggregate view for last 24 hours
            var query = $@"
                SELECT 
                    bucket,
                    tag,
                    AVG(avg_value) as avg_value
                FROM sensor_trends_1hour
                WHERE {whereClause}
                GROUP BY bucket, tag
                ORDER BY bucket ASC";

            var connection = _dbContext.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);

            var results = new List<object>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = query;
                var factoryParam = command.CreateParameter();
                factoryParam.ParameterName = "@factoryId";
                factoryParam.Value = (int)factoryId;
                command.Parameters.Add(factoryParam);

                if (tag != null)
                {
                    var tagParam = command.CreateParameter();
                    tagParam.ParameterName = "@tag";
                    tagParam.Value = tag;
                    command.Parameters.Add(tagParam);
                }

                if (lineNumber.HasValue)
                {
                    var linePatternParam = command.CreateParameter();
                    linePatternParam.ParameterName = "@linePattern";
                    linePatternParam.Value = $"%line{lineNumber.Value}";
                    command.Parameters.Add(linePatternParam);
                }

                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var bucket = reader.GetDateTime(0);
                        var tagValue = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        var avgValue = reader.IsDBNull(2) ? 0.0 : Convert.ToDouble(reader.GetDecimal(2));

                        results.Add(new
                        {
                            Time = bucket.ToString("HH:mm"),
                            Value = Math.Round(avgValue, 2)
                        });
                    }
                }
            }

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sensor trends");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

