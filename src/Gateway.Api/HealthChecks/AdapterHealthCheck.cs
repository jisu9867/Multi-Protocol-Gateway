using Gateway.Core.Adapters;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gateway.Api.HealthChecks;

/// <summary>
/// Health check for adapters
/// </summary>
public sealed class AdapterHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IAdapter> _adapters;

    public AdapterHealthCheck(IEnumerable<IAdapter> adapters)
    {
        _adapters = adapters;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var adapters = _adapters.ToList();
        if (adapters.Count == 0)
        {
            return HealthCheckResult.Unhealthy("No adapters configured");
        }

        var adapterResults = new List<(IAdapter Adapter, AdapterHealth? Health, Exception? Exception)>();
        foreach (var adapter in adapters)
        {
            try
            {
                var health = await adapter.GetHealthAsync(cancellationToken);
                adapterResults.Add((adapter, health, null));
            }
            catch (Exception ex)
            {
                adapterResults.Add((adapter, null, ex));
            }
        }

        var healthyCount = adapterResults.Count(r => r.Health?.Status == AdapterStatus.Running);
        var totalCount = adapters.Count;

        var adapterInfos = adapterResults.Select(r => new Dictionary<string, object>
        {
            ["id"] = r.Adapter.Id,
            ["status"] = r.Health?.Status.ToString() ?? "Unknown",
            ["error"] = r.Exception?.Message ?? (object)""
        }).ToList();

        if (healthyCount == totalCount)
        {
            return HealthCheckResult.Healthy(
                $"All {totalCount} adapters are running",
                new Dictionary<string, object>
                {
                    ["adapters"] = adapterInfos
                });
        }
        else if (healthyCount > 0)
        {
            return HealthCheckResult.Degraded(
                $"{healthyCount}/{totalCount} adapters are running",
                exception: null,
                data: new Dictionary<string, object>
                {
                    ["adapters"] = adapterInfos
                });
        }
        else
        {
            return HealthCheckResult.Unhealthy(
                "No adapters are running",
                exception: null,
                data: new Dictionary<string, object>
                {
                    ["adapters"] = adapterInfos
                });
        }
    }
}
