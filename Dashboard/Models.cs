namespace HomeManagementDashboard;

/// <summary>A device the dashboard should monitor.</summary>
public class DeviceRegistration
{
    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;
}

/// <summary>Bound from the "Monitoring" section of appsettings.json.</summary>
public class MonitoringSettings
{
    public int PollingIntervalSeconds { get; set; } = 5;

    public int RequestTimeoutSeconds { get; set; } = 3;

    public List<DeviceRegistration> Devices { get; set; } = [];
}

public enum DeviceStatus
{
    Unknown,
    Online,
    Offline
}

// Shapes of the JSON returned by the device API (/info and /config).
public record DeviceInfoResponse(string? Name, string? Address, Dictionary<string, string>? Metrics);

public record DeviceActionResponse(string? Action);

public record DeviceConfigResponse(string? Name, List<DeviceActionResponse>? Actions);

/// <summary>The latest known state of one device.</summary>
public record DeviceSnapshot(
    string Name,
    string Address,
    DeviceStatus Status,
    string? ReportedName,
    int? UptimeSeconds,
    double? Temperature,
    long? ResponseTimeMs,
    DateTimeOffset? LastSeen,
    DateTimeOffset? LastChecked,
    string? Error,
    IReadOnlyList<string> Actions)
{
    public static DeviceSnapshot New(string name, string address) =>
        new(name, address, DeviceStatus.Unknown, null, null, null, null, null, null, null, []);
}

/// <summary>Everything the dashboard page needs in one response.</summary>
public record DashboardSummary(
    int TotalDevices,
    int OnlineDevices,
    int OfflineDevices,
    double? AverageTemperature,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DeviceSnapshot> Devices)
{
    public static DashboardSummary From(IReadOnlyList<DeviceSnapshot> devices)
    {
        var temperatures = devices
            .Where(d => d.Status == DeviceStatus.Online && d.Temperature.HasValue)
            .Select(d => d.Temperature!.Value)
            .ToList();

        return new DashboardSummary(
            devices.Count,
            devices.Count(d => d.Status == DeviceStatus.Online),
            devices.Count(d => d.Status == DeviceStatus.Offline),
            temperatures.Count > 0 ? Math.Round(temperatures.Average(), 1) : null,
            DateTimeOffset.UtcNow,
            devices);
    }
}
