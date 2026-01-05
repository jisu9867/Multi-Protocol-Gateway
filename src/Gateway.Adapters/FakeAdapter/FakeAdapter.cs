using System.Diagnostics;
using Gateway.Core.Adapters;
using Microsoft.Extensions.Logging;

namespace Gateway.Adapters.FakeAdapter;

/// <summary>
/// Fake adapter for testing - generates telemetry data every 100ms
/// </summary>
public sealed class FakeAdapter : IAdapter
{
    private static readonly ActivitySource ActivitySource = new("Gateway.Adapters.FakeAdapter");
    
    private readonly ILogger<FakeAdapter> _logger;
    private readonly string _id;
    private Task? _generationTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private AdapterStatus _status = AdapterStatus.Stopped;
    private long _sequence = 0;
    private readonly object _sequenceLock = new();

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
                ["status"] = _status.ToString(),
                ["sequence"] = _sequence
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
        var tags = new[] { "temp", "humidity", "pressure" };
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Create Activity for distributed tracing
                using var activity = ActivitySource.StartActivity("FakeAdapter.GenerateData");
                activity?.SetTag("adapter.id", _id);
                
                var tag = tags[random.Next(tags.Length)];
                var value = tag switch
                {
                    "temp" => random.NextDouble() * 50 + 20, // 20-70°C
                    "humidity" => random.NextDouble() * 100, // 0-100%
                    "pressure" => random.NextDouble() * 200 + 900, // 900-1100 hPa
                    _ => random.NextDouble() * 100
                };

                // Get current sequence and increment
                long currentSequence;
                lock (_sequenceLock)
                {
                    currentSequence = _sequence++;
                }

                // Get TraceId from Activity
                var traceId = Activity.Current?.Id ?? activity?.Id;

                var payload = new Dictionary<string, object>
                {
                    ["value"] = value
                };

                if (DataHandler != null)
                {
                    var metadata = new Dictionary<string, string>
                    {
                        ["tag"] = tag,
                        ["sequence"] = currentSequence.ToString()
                    };
                    
                    if (!string.IsNullOrEmpty(traceId))
                    {
                        metadata["traceId"] = traceId;
                    }

                    await DataHandler.HandleDataAsync(
                        _id, 
                        "sim-1", 
                        DateTime.UtcNow, 
                        payload, 
                        metadata,
                        cancellationToken).ConfigureAwait(false);
                }

                // Generate data every 100ms
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
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
