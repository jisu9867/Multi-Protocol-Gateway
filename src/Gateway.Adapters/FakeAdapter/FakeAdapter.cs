using Gateway.Core.Adapters;
using Microsoft.Extensions.Logging;

namespace Gateway.Adapters.FakeAdapter;

/// <summary>
/// Fake adapter for testing - generates random telemetry data
/// </summary>
public sealed class FakeAdapter : IAdapter
{
    private readonly ILogger<FakeAdapter> _logger;
    private readonly string _id;
    private Task? _generationTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private AdapterStatus _status = AdapterStatus.Stopped;

    public FakeAdapter(
        string id,
        ILogger<FakeAdapter> logger,
        IAdapterDataHandler? dataHandler = null)
    {
        _id = id;
        _logger = logger;
        DataHandler = dataHandler;
    }

    public string Id => _id;
    public AdapterStatus Status => _status;
    public IAdapterDataHandler? DataHandler { get; set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_status == AdapterStatus.Running)
        {
            return Task.CompletedTask;
        }

        _status = AdapterStatus.Starting;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _generationTask = GenerateDataAsync(_cancellationTokenSource.Token);
        _status = AdapterStatus.Running;

        _logger.LogInformation("FakeAdapter {AdapterId} started", _id);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_status != AdapterStatus.Running)
        {
            return;
        }

        _status = AdapterStatus.Stopping;
        
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
        }

        if (_generationTask != null)
        {
            try
            {
                await _generationTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _cancellationTokenSource?.Dispose();
        _generationTask = null;
        _status = AdapterStatus.Stopped;

        _logger.LogInformation("FakeAdapter {AdapterId} stopped", _id);
    }

    public Task<AdapterHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var health = new AdapterHealth
        {
            Status = _status,
            Metrics = new Dictionary<string, object>
            {
                ["status"] = _status.ToString()
            }
        };

        if (_status == AdapterStatus.Faulted)
        {
            health.ErrorMessage = "Adapter is in faulted state";
        }

        return Task.FromResult(health);
    }

    private async Task GenerateDataAsync(CancellationToken cancellationToken)
    {
        var random = new Random();
        var sourceIds = new[] { "device-001", "device-002", "device-003" };
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var sourceId = sourceIds[random.Next(sourceIds.Length)];
                var payload = new Dictionary<string, object>
                {
                    ["temperature"] = random.NextDouble() * 50 + 20, // 20-70°C
                    ["humidity"] = random.NextDouble() * 100, // 0-100%
                    ["pressure"] = random.NextDouble() * 200 + 900, // 900-1100 hPa
                    ["vibration"] = random.NextDouble() * 10 // 0-10 m/s²
                };

                if (DataHandler != null)
                {
                    await DataHandler.HandleDataAsync(_id, sourceId, DateTime.UtcNow, payload, cancellationToken)
                        .ConfigureAwait(false);
                }

                // Generate data every 1-3 seconds
                var delay = TimeSpan.FromSeconds(random.NextDouble() * 2 + 1);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating fake data in adapter {AdapterId}", _id);
                _status = AdapterStatus.Faulted;
                break;
            }
        }
    }
}

