using Microsoft.AspNetCore.SignalR;

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
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
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
    }

    /// <summary>
    /// Unsubscribe from sensor updates
    /// </summary>
    public async Task UnsubscribeFromSensor(string factoryId, string tag, string? sourceId = null)
    {
        var groupName = GetGroupName(factoryId, tag, sourceId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Client {ConnectionId} unsubscribed from {GroupName}", Context.ConnectionId, groupName);
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

