using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace HomeManagementDashboard;

/// <summary>Calls each device's /info (and /config) endpoint and records whether it is online.</summary>
public class DeviceChecker(IHttpClientFactory httpClientFactory, DeviceStore store, ILogger<DeviceChecker> logger)
{
    public const string HttpClientName = "devices";

    public async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        var devices = store.GetAll();
        await Task.WhenAll(devices.Select(device => RefreshAsync(device, cancellationToken)));
    }

    public async Task<DeviceSnapshot?> RefreshAsync(string name, CancellationToken cancellationToken)
    {
        var device = store.Get(name);
        return device is null ? null : await RefreshAsync(device, cancellationToken);
    }

    private async Task<DeviceSnapshot> RefreshAsync(DeviceSnapshot device, CancellationToken cancellationToken)
    {
        var updated = await CheckAsync(device, cancellationToken);

        if (updated.Status != device.Status)
        {
            logger.LogInformation("Device {Device} ({Address}) is now {Status}. {Error}",
                updated.Name, updated.Address, updated.Status, updated.Error);
        }

        store.Update(updated);
        return updated;
    }

    private async Task<DeviceSnapshot> CheckAsync(DeviceSnapshot device, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var checkedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var info = await client.GetFromJsonAsync<DeviceInfoResponse>($"{device.Address}/info", cancellationToken);
            stopwatch.Stop();

            // The list of actions rarely changes, so only ask for it until we have it.
            var actions = device.Actions.Count > 0
                ? device.Actions
                : await TryGetActionsAsync(client, device.Address, cancellationToken);

            return device with
            {
                Status = DeviceStatus.Online,
                ReportedName = info?.Name,
                UptimeSeconds = ReadUptime(info?.Metrics),
                Temperature = ReadTemperature(info?.Metrics),
                ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                LastSeen = checkedAt,
                LastChecked = checkedAt,
                Error = null,
                Actions = actions
            };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsConnectionProblem(ex))
        {
            return device with
            {
                Status = DeviceStatus.Offline,
                ResponseTimeMs = null,
                LastChecked = checkedAt,
                Error = Describe(ex)
            };
        }
    }

    private static async Task<IReadOnlyList<string>> TryGetActionsAsync(HttpClient client, string address, CancellationToken cancellationToken)
    {
        try
        {
            var config = await client.GetFromJsonAsync<DeviceConfigResponse>($"{address}/config", cancellationToken);
            return config?.Actions?
                .Select(a => a.Action)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!)
                .ToList() ?? [];
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsConnectionProblem(ex))
        {
            return [];
        }
    }

    private static bool IsConnectionProblem(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException or InvalidOperationException;

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "No response (timed out)",
        JsonException or NotSupportedException => "Unexpected response from device",
        _ => ex.Message
    };

    // The device API reports 0 when it cannot read the value (e.g. not running on Linux),
    // so 0 is shown as "not available" rather than as a real reading.
    private static int? ReadUptime(Dictionary<string, string>? metrics) =>
        double.TryParse(ReadMetric(metrics, "UptimeSeconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? (int)value
            : null;

    private static double? ReadTemperature(Dictionary<string, string>? metrics) =>
        double.TryParse(ReadMetric(metrics, "Temperature"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value != 0
            ? Math.Round(value, 1)
            : null;

    private static string? ReadMetric(Dictionary<string, string>? metrics, string key) =>
        metrics?.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
}
