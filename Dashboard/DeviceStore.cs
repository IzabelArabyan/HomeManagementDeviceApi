using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace HomeManagementDashboard;

/// <summary>Thread-safe, in-memory list of monitored devices and their latest state.</summary>
public class DeviceStore
{
    private readonly ConcurrentDictionary<string, DeviceSnapshot> _devices = new(StringComparer.OrdinalIgnoreCase);

    public DeviceStore(IOptions<MonitoringSettings> options)
    {
        foreach (var device in options.Value.Devices)
        {
            if (!string.IsNullOrWhiteSpace(device.Name) && !string.IsNullOrWhiteSpace(device.Address))
            {
                TryAdd(device.Name, device.Address);
            }
        }
    }

    public bool TryAdd(string name, string address) =>
        _devices.TryAdd(name.Trim(), DeviceSnapshot.New(name.Trim(), NormalizeAddress(address)));

    public bool Remove(string name) => _devices.TryRemove(name, out _);

    public DeviceSnapshot? Get(string name) => _devices.TryGetValue(name, out var device) ? device : null;

    public IReadOnlyList<DeviceSnapshot> GetAll() =>
        _devices.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Stores a new state, unless the device was removed while it was being checked.</summary>
    public void Update(DeviceSnapshot snapshot)
    {
        if (_devices.TryGetValue(snapshot.Name, out var existing))
        {
            _devices.TryUpdate(snapshot.Name, snapshot, existing);
        }
    }

    public static string NormalizeAddress(string address) => address.Trim().TrimEnd('/');
}
