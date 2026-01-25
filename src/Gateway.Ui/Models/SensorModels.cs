namespace Gateway.Ui.Models;

public class SensorData
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public SensorStatus Status { get; set; }
    public double MinThreshold { get; set; }
    public double MaxThreshold { get; set; }
    public DateTime LastUpdated { get; set; }
}

public enum SensorStatus
{
    Normal,
    Warning,
    Critical,
    Offline
}

public class KpiData
{
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double Target { get; set; }
    public double Change { get; set; }
    public string Icon { get; set; } = string.Empty;
}

public class Factory
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public FactoryStatus Status { get; set; }
    public int ActiveSensors { get; set; }
    public int TotalSensors { get; set; }
    public double Oee { get; set; }
    public List<SensorData> Sensors { get; set; } = new();
    public List<KpiData> Kpis { get; set; } = new();
}

public enum FactoryStatus
{
    Online,
    Warning,
    Critical,
    Offline
}

public class Alert
{
    public string Id { get; set; } = string.Empty;
    public string FactoryId { get; set; } = string.Empty;
    public string FactoryName { get; set; } = string.Empty;
    public string SensorId { get; set; } = string.Empty;
    public string SensorName { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsAcknowledged { get; set; }
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public class ChartDataPoint
{
    public string Time { get; set; } = string.Empty;
    public double Value { get; set; }
}
