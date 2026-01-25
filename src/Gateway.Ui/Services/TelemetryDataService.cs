using Gateway.Core.Models;
using Gateway.Ui.Models;
using System.Text.Json;
using FactoryEnum = Gateway.Core.Models.Factory;
using FactoryModel = Gateway.Ui.Models.Factory;

namespace Gateway.Ui.Services;

public class TelemetryDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelemetryDataService> _logger;

    public TelemetryDataService(HttpClient httpClient, ILogger<TelemetryDataService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        
        // Verify BaseAddress is set
        if (_httpClient.BaseAddress == null)
        {
            _logger.LogError("HttpClient BaseAddress is not set!");
        }
        else
        {
            _logger.LogInformation("HttpClient BaseAddress: {BaseAddress}", _httpClient.BaseAddress);
        }
    }

    /// <summary>
    /// Get list of all factories
    /// </summary>
    public async Task<List<FactoryModel>> GetFactoriesAsync(CancellationToken cancellationToken = default)
    {
        var factories = new List<FactoryModel>();
        var factoryIds = new[] { "Ulsan", "Asan", "Jeonju" };

        foreach (var factoryId in factoryIds)
        {
            var factory = await GetFactoryAsync(factoryId, null, false, cancellationToken);
            if (factory != null)
            {
                factories.Add(factory);
            }
        }

        return factories;
    }

    /// <summary>
    /// Get factory information with sensor readings
    /// </summary>
    /// <param name="factoryId">Factory ID</param>
    /// <param name="lineNumber">Optional line number filter (e.g., 1, 2, 3)</param>
    /// <param name="groupByLine">If true, groups results by line. If false, returns one sensor per tag.</param>
    public async Task<FactoryModel?> GetFactoryAsync(string factoryId, int? lineNumber = null, bool groupByLine = false, CancellationToken cancellationToken = default)
    {
        try
        {
            // Parse factory ID to Factory enum - support multiple formats
            var factoryEnum = ParseFactoryId(factoryId);
            if (!factoryEnum.HasValue)
            {
                _logger.LogWarning("Invalid factory ID: {FactoryId}", factoryId);
                return null;
            }

            var factoryEnumValue = factoryEnum.Value;

            // Build query string with optional line filter
            var queryParams = new List<string> { $"factoryId={factoryEnumValue}" };
            if (lineNumber.HasValue)
            {
                queryParams.Add($"lineNumber={lineNumber.Value}");
            }
            if (groupByLine)
            {
                queryParams.Add("groupByLine=true");
            }
            
            var queryString = string.Join("&", queryParams);
            
            // Get sensor readings - ensure path starts with /
            var readingsResponse = await _httpClient.GetAsync(
                $"/api/events/sensor-readings?{queryString}", 
                cancellationToken);

            if (!readingsResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get sensor readings: {StatusCode}", readingsResponse.StatusCode);
                return null;
            }

            var readingsJson = await readingsResponse.Content.ReadAsStringAsync(cancellationToken);
            var readings = JsonSerializer.Deserialize<List<SensorReadingDto>>(readingsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<SensorReadingDto>();

            // Map to sensor data
            var sensors = MapToSensorData(readings);

            // Calculate factory status based on sensor statuses
            var factoryStatus = CalculateFactoryStatus(sensors);

            // Map to Factory model
            var factory = new FactoryModel
            {
                Id = factoryId.ToLower(),
                Name = GetFactoryName(factoryEnumValue),
                Location = GetFactoryLocation(factoryEnumValue),
                Status = factoryStatus,
                ActiveSensors = readings.Count,
                TotalSensors = readings.Count,
                Oee = 0, // TODO: Calculate OEE
                Sensors = sensors,
                Kpis = GenerateKpis() // TODO: Calculate from actual data
            };

            return factory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting factory data for {FactoryId}", factoryId);
            return null;
        }
    }

    /// <summary>
    /// Get chart data for sensor trends (24 hours, 1-hour intervals)
    /// </summary>
    /// <param name="sensorType">Sensor type (e.g., "temperature", "power")</param>
    /// <param name="factoryId">Factory ID</param>
    /// <param name="lineNumber">Optional line number filter (e.g., 1, 2, 3)</param>
    public async Task<List<ChartDataPoint>> GetChartDataAsync(string sensorType, FactoryEnum factoryId, int? lineNumber = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Map sensor type to tag
            var tag = MapSensorTypeToTag(sensorType);

            // Build query string with optional line filter
            var queryParams = new List<string> { $"factoryId={factoryId}", $"tag={tag}" };
            if (lineNumber.HasValue)
            {
                queryParams.Add($"lineNumber={lineNumber.Value}");
            }
            
            var queryString = string.Join("&", queryParams);

            var response = await _httpClient.GetAsync(
                $"/api/events/sensor-trends?{queryString}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get sensor trends: {StatusCode}", response.StatusCode);
                return new List<ChartDataPoint>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var trends = JsonSerializer.Deserialize<List<TrendDataPointDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<TrendDataPointDto>();

            return trends.Select(t => new ChartDataPoint
            {
                Time = t.Time,
                Value = t.Value
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chart data for {SensorType}", sensorType);
            return new List<ChartDataPoint>();
        }
    }

    private List<SensorData> MapToSensorData(List<SensorReadingDto> readings)
    {
        return readings.Select(r => new SensorData
        {
            Id = r.SourceId, // Use source_id as ID to preserve line information
            Name = GetSensorName(r.Tag), // Remove line information from name
            Type = MapTagToSensorType(r.Tag),
            Value = r.Value,
            Unit = GetSensorUnit(r.Tag),
            Status = DetermineSensorStatus(r.Value, r.Tag),
            MinThreshold = GetMinThreshold(r.Tag),
            MaxThreshold = GetMaxThreshold(r.Tag),
            LastUpdated = r.LastUpdated
        }).ToList();
    }

    private string? ExtractLineName(string sourceId)
    {
        // Extract line name from source_id format: {factory}-line{number}
        if (string.IsNullOrEmpty(sourceId))
            return null;

        var parts = sourceId.Split('-');
        foreach (var part in parts)
        {
            if (part.StartsWith("line", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(part.Substring(4), out var lineNumber))
                {
                    return $"Line {lineNumber}";
                }
            }
        }
        return null;
    }

    private SensorStatus DetermineSensorStatus(double value, string tag)
    {
        var min = GetMinThreshold(tag);
        var max = GetMaxThreshold(tag);
        var range = max - min;
        
        // 범위가 없으면 Critical
        if (range <= 0)
            return SensorStatus.Critical;
        
        // Progress width 계산 (SensorCard의 GetProgressWidth와 동일)
        var calculatedWidth = (value - min) / range * 100;
        var progressWidth = calculatedWidth < 0 || calculatedWidth > 100 ? 0 : calculatedWidth;
        
        // Bar가 표시 안 되는 경우 (width가 0% 이하) -> Critical
        if (progressWidth <= 0)
            return SensorStatus.Critical;
        
        var position = (value - min) / range;
        
        // Critical: 범위를 벗어나거나 경계 10% 이내 -> 빨간색 bar
        if (value < min || value > max || position > 0.9 || position < 0.1)
            return SensorStatus.Critical;
        
        // Warning: 경계 20% 이내 (0.1-0.2 또는 0.8-0.9) -> 노란색 bar
        if (position > 0.8 || position < 0.2)
            return SensorStatus.Warning;
        
        return SensorStatus.Normal;
    }

    private string MapTagToSensorType(string tag)
    {
        return tag.ToLower() switch
        {
            "temp" => "temperature",
            "humidity" => "humidity",
            "pressure" => "pressure",
            "vibration" => "vibration",
            "power" => "power",
            "flow" => "flow",
            _ => tag.ToLower()
        };
    }

    private string MapSensorTypeToTag(string sensorType)
    {
        return sensorType.ToLower() switch
        {
            "temperature" => "temp",
            "humidity" => "humidity",
            "pressure" => "pressure",
            "vibration" => "vibration",
            "power" => "power",
            "flow" => "flow",
            _ => sensorType.ToLower()
        };
    }

    private string GetSensorName(string tag)
    {
        return tag.ToLower() switch
        {
            "temp" => "Temperature",
            "humidity" => "Humidity",
            "pressure" => "Pressure",
            "vibration" => "Vibration",
            "power" => "Power",
            "flow" => "Flow Rate",
            _ => tag
        };
    }

    private string GetSensorUnit(string tag)
    {
        return tag.ToLower() switch
        {
            "temp" => "°C",
            "humidity" => "%",
            "pressure" => "hPa",
            "vibration" => "mm/s",
            "power" => "kW",
            "flow" => "L/min",
            _ => ""
        };
    }

    private double GetMinThreshold(string tag)
    {
        return tag.ToLower() switch
        {
            "temp" => 20,      // Adjusted: 20-70°C range
            "humidity" => 30,  // 30-80% range
            "pressure" => 900, // 900-1100 hPa range
            "vibration" => 0,  // 0-1.0 mm/s range
            "power" => 200,    // 200-300 kW range
            "flow" => 100,     // 100-150 L/min range
            _ => 0
        };
    }

    private double GetMaxThreshold(string tag)
    {
        return tag.ToLower() switch
        {
            "temp" => 70,      // Adjusted: 20-70°C range
            "humidity" => 80,  // Adjusted: 30-80% range
            "pressure" => 1100, // Adjusted: 900-1100 hPa range
            "vibration" => 1.0, // 0-1.0 mm/s range
            "power" => 300,    // 200-300 kW range
            "flow" => 150,     // 100-150 L/min range
            _ => 100
        };
    }

    private FactoryStatus CalculateFactoryStatus(List<SensorData> sensors)
    {
        if (sensors == null || sensors.Count == 0)
        {
            _logger.LogWarning("No sensors found, returning Critical status");
            return FactoryStatus.Critical; // No sensors = Critical
        }

        var criticalCount = sensors.Count(s => s.Status == SensorStatus.Critical);
        var warningCount = sensors.Count(s => s.Status == SensorStatus.Warning);
        var normalCount = sensors.Count(s => s.Status == SensorStatus.Normal);

        _logger.LogInformation("Factory Status Calculation: Critical={CriticalCount}, Warning={WarningCount}, Normal={NormalCount}", 
            criticalCount, warningCount, normalCount);

        if (criticalCount > 0)
        {
            _logger.LogInformation("Factory Status: Critical (has {Count} critical sensors)", criticalCount);
            return FactoryStatus.Critical;
        }
        if (warningCount > 0)
        {
            _logger.LogInformation("Factory Status: Warning (has {Count} warning sensors)", warningCount);
            return FactoryStatus.Warning;
        }
        
        _logger.LogInformation("Factory Status: Online (all sensors normal)");
        return FactoryStatus.Online;
    }

    private FactoryEnum? ParseFactoryId(string factoryId)
    {
        if (string.IsNullOrWhiteSpace(factoryId))
            return null;

        // Try direct enum parse first (e.g., "Ulsan", "Asan", "Jeonju")
        if (Enum.TryParse<FactoryEnum>(factoryId, ignoreCase: true, out var directEnum))
        {
            return directEnum;
        }

        // Try numeric parse (e.g., "1", "2", "3")
        if (int.TryParse(factoryId, out var numericValue) && Enum.IsDefined(typeof(FactoryEnum), numericValue))
        {
            return (FactoryEnum)numericValue;
        }

        // Try legacy format mapping (e.g., "factory-a" -> Ulsan)
        var lowerId = factoryId.ToLower();
        return lowerId switch
        {
            "factory-a" or "a" => FactoryEnum.Ulsan,
            "factory-b" or "b" => FactoryEnum.Asan,
            "factory-c" or "c" => FactoryEnum.Jeonju,
            "factory-d" or "d" => FactoryEnum.Hwaseong,
            _ => null
        };
    }

    private string GetFactoryName(FactoryEnum factory)
    {
        return factory switch
        {
            FactoryEnum.Ulsan => "Factory A",
            FactoryEnum.Asan => "Factory B",
            FactoryEnum.Jeonju => "Factory C",
            FactoryEnum.Hwaseong => "Factory D",
            _ => factory.ToString()
        };
    }

    private string GetFactoryLocation(FactoryEnum factory)
    {
        return factory switch
        {
            FactoryEnum.Ulsan => "Ulsan, Korea",
            FactoryEnum.Asan => "Asan, Korea",
            FactoryEnum.Jeonju => "Jeonju, Korea",
            FactoryEnum.Hwaseong => "Hwaseong, Korea",
            _ => "Korea"
        };
    }

    private List<KpiData> GenerateKpis()
    {
        // TODO: Calculate from actual data
        return new List<KpiData>
        {
            new KpiData { Name = "OEE", Value = 91.2, Unit = "%", Target = 95, Change = 2.5, Icon = "gauge" },
            new KpiData { Name = "Uptime", Value = 99.1, Unit = "%", Target = 99.5, Change = 0.3, Icon = "clock" },
            new KpiData { Name = "Throughput", Value = 1247, Unit = "units/hr", Target = 1300, Change = 5.2, Icon = "trending-up" },
            new KpiData { Name = "Defect Rate", Value = 0.8, Unit = "%", Target = 1, Change = -0.2, Icon = "alert-circle" }
        };
    }

    private class SensorReadingDto
    {
        public string Tag { get; set; } = string.Empty;
        public string EquipmentType { get; set; } = string.Empty;
        public string EquipmentName { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public double Value { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public long Count { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    private class TrendDataPointDto
    {
        public string Time { get; set; } = string.Empty;
        public double Value { get; set; }
    }
}


