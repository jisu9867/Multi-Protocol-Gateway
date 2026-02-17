using Microsoft.AspNetCore.SignalR;
using Gateway.Infrastructure.Observability;

namespace Gateway.Api.Hubs;

/// <summary>
/// SignalR Hub for real-time telemetry data streaming
/// </summary>
public class TelemetryHub : Hub
{
    private readonly ILogger<TelemetryHub> _logger;

    public TelemetryHub(ILogger<TelemetryHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        // Initialize metrics for connected clients (use "unknown" for factory/line/tag until subscription)
        // This ensures metrics are always visible in Prometheus
        SignalRMetrics.UpdateConnectedClients("unknown", "unknown", "unknown", GetTotalConnectedClients());
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        // Update metrics for disconnected clients
        SignalRMetrics.UpdateConnectedClients("unknown", "unknown", "unknown", GetTotalConnectedClients());
        return base.OnDisconnectedAsync(exception);
    }

    private int GetTotalConnectedClients()
    {
        // Note: This is an approximation. In a production system, you might want to track this more accurately.
        // For now, we'll use a simple approach and update it when clients connect/disconnect.
        // The actual count should be tracked by the HubContext, but we'll use a placeholder for now.
        return 1; // Placeholder - will be updated when we have proper tracking
    }

    /// <summary>
    /// Subscribe to real-time updates for a specific sensor
    /// </summary>
    /// <param name="factoryId">Factory ID</param>
    /// <param name="tag">Sensor tag (e.g., "temp", "power")</param>
    /// <param name="sourceId">Optional source ID filter (e.g., "ulsan-line1")</param>
    public async Task SubscribeToSensor(string factoryId, string tag, string? sourceId = null)
    {
        var groupName = GetGroupName(factoryId, tag, sourceId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Client {ConnectionId} subscribed to {GroupName} (Factory={FactoryId}, Tag={Tag}, SourceId={SourceId})", 
            Context.ConnectionId, groupName, factoryId, tag, sourceId ?? "all");
        
        // Update metrics for connected clients with specific factory/line/tag
        var normalizedFactoryId = factoryId?.ToLowerInvariant() ?? "unknown";
        var normalizedTag = tag?.ToLowerInvariant() ?? "unknown";
        var lineId = sourceId != null ? MetricLabelHelper.ExtractLineId(sourceId) : "unknown";
        
        // Get approximate client count for this group (this is a simplified approach)
        // In production, you might want to track this more accurately
        var clientCount = 1; // Placeholder - actual count would require tracking group membership
        SignalRMetrics.UpdateConnectedClients(normalizedFactoryId, lineId, normalizedTag, clientCount);
    }

    /// <summary>
    /// Unsubscribe from sensor updates
    /// </summary>
    public async Task UnsubscribeFromSensor(string factoryId, string tag, string? sourceId = null)
    {
        var groupName = GetGroupName(factoryId, tag, sourceId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Client {ConnectionId} unsubscribed from {GroupName}", Context.ConnectionId, groupName);
        
        // Update metrics for disconnected clients
        var normalizedFactoryId = factoryId?.ToLowerInvariant() ?? "unknown";
        var normalizedTag = tag?.ToLowerInvariant() ?? "unknown";
        var lineId = sourceId != null ? MetricLabelHelper.ExtractLineId(sourceId) : "unknown";
        
        // Set to 0 or decrement - simplified approach
        SignalRMetrics.UpdateConnectedClients(normalizedFactoryId, lineId, normalizedTag, 0);
    }

    private static string GetGroupName(string factoryId, string tag, string? sourceId)
    {
        // Normalize factoryId and sourceId to lowercase for consistent group naming
        var normalizedFactoryId = factoryId?.ToLowerInvariant() ?? "";
        var normalizedTag = tag?.ToLowerInvariant() ?? "";
        
        if (!string.IsNullOrEmpty(sourceId))
        {
            var normalizedSourceId = sourceId.ToLowerInvariant();
            return $"{normalizedFactoryId}:{normalizedTag}:{normalizedSourceId}";
        }
        return $"{normalizedFactoryId}:{normalizedTag}";
    }
}

