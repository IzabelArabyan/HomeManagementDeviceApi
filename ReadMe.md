# Home Management – Device API + Monitoring Dashboard

Author: Izabel Arabyan

The solution has two projects:

| Project | Folder | What it does |
|---|---|---|
| `HomeManagementDeviceApi` | repository root | A virtual smart device. Each launch profile (`Device1`, `Device2`) is one device exposing `/info`, `/config`, `/command`, `/shutdown`, `/restart`. |
| `HomeManagementDashboard` | `Dashboard/` | A monitoring dashboard that polls every device and shows its status, the **number of online devices**, uptime, temperature, response time and available actions. |

## Requirements

- .NET 11 SDK (or .NET 10 after changing `TargetFramework` to `net10.0` in both `.csproj` files)

## Build

```cmd
dotnet build HomeManagementDeviceApi.slnx
```

## Run the virtual devices

Run each in its own terminal from the repository root:

```cmd
dotnet run --launch-profile Device1

dotnet run --launch-profile Device2
```

Open `http://localhost:1235/info` and `http://localhost:1237/info`. You should see the json output.

## Run the dashboard

In a third terminal:

```cmd
dotnet run --project Dashboard
```

Open `http://localhost:5100`.

The dashboard checks every device every 5 seconds (and the page updates every 3 seconds):

- **Online / Offline / Total devices** counters and an online bar
- **Average temperature** of online devices
- A table with status, address, uptime, temperature, response time, last seen and the device's actions (from `/config`)
- **Connect a device** form to add a device at runtime, and a **Remove** button
- **Refresh now** button to check all devices immediately

`LiveCamera` (`http://localhost:1239`) is configured but not started, so it shows as **Offline** – stop `Device2` and it will turn offline within a few seconds too.

> Uptime and temperature are read from `/proc` and `/sys`, so they only have values when a device runs on Linux (e.g. a Raspberry Pi or Docker). On Windows/macOS the dashboard shows `n/a`.

### Configuration

Devices and timings are in `Dashboard/appsettings.json`:

```json
"Monitoring": {
  "PollingIntervalSeconds": 5,
  "RequestTimeoutSeconds": 3,
  "Devices": [
    { "Name": "Device1", "Address": "http://localhost:1235" },
    { "Name": "Device2", "Address": "http://localhost:1237" }
  ]
}
```

### Dashboard API

| Method | URL | Description |
|---|---|---|
| GET | `/api/dashboard` | Counts (total / online / offline), average temperature and all devices |
| GET | `/api/devices` | All devices with their latest state |
| GET | `/api/devices/{name}` | One device |
| POST | `/api/devices` | Add a device: `{ "name": "Device3", "address": "http://localhost:1239" }` |
| DELETE | `/api/devices/{name}` | Remove a device |
| POST | `/api/refresh` | Check all devices now |
