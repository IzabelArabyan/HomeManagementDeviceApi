using System.Net.Security;
using System.Text.Json.Serialization;
using HomeManagementDashboard;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MonitoringSettings>(builder.Configuration.GetSection("Monitoring"));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The device API redirects http -> https and uses the ASP.NET Core development certificate.
// In Development we accept that "localhost" certificate even if it is not trusted on this machine.
var acceptLocalDevCertificate = builder.Environment.IsDevelopment();

builder.Services
    .AddHttpClient(DeviceChecker.HttpClientName, (services, client) =>
    {
        var settings = services.GetRequiredService<IOptions<MonitoringSettings>>().Value;
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None
                || (acceptLocalDevCertificate && certificate?.Subject == "CN=localhost")
        }
    });

builder.Services.AddSingleton<DeviceStore>();
builder.Services.AddSingleton<DeviceChecker>();
builder.Services.AddHostedService<DeviceMonitor>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

// Summary for the dashboard: counts (including online devices) plus the state of every device.
api.MapGet("/dashboard", (DeviceStore store) => DashboardSummary.From(store.GetAll()));

api.MapGet("/devices", (DeviceStore store) => store.GetAll());

api.MapGet("/devices/{name}", (string name, DeviceStore store) =>
    store.Get(name) is { } device ? Results.Ok(device) : Results.NotFound());

// Connect a new device, e.g. { "name": "Device3", "address": "http://localhost:1239" }
api.MapPost("/devices", async (DeviceRegistration request, DeviceStore store, DeviceChecker checker, CancellationToken token) =>
{
    var name = (request.Name ?? string.Empty).Trim();
    var address = (request.Address ?? string.Empty).Trim();
    if (name.Length == 0)
    {
        return Results.BadRequest(new { error = "Name is required." });
    }

    if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
        return Results.BadRequest(new { error = "Address must be a full http:// or https:// URL." });
    }

    if (!store.TryAdd(name, address))
    {
        return Results.Conflict(new { error = $"A device named '{name}' already exists." });
    }

    var device = await checker.RefreshAsync(name, token);
    return Results.Created($"/api/devices/{Uri.EscapeDataString(name)}", device);
});

api.MapDelete("/devices/{name}", (string name, DeviceStore store) =>
    store.Remove(name) ? Results.NoContent() : Results.NotFound());

// Check every device right now instead of waiting for the next poll.
api.MapPost("/refresh", async (DeviceStore store, DeviceChecker checker, CancellationToken token) =>
{
    await checker.RefreshAllAsync(token);
    return DashboardSummary.From(store.GetAll());
});

app.Run();
