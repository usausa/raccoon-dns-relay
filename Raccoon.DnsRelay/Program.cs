using System.Runtime;

using Microsoft.Extensions.Options;

using Raccoon.DnsRelay;
using Raccoon.DnsRelay.Caching;
using Raccoon.DnsRelay.Configuration;
using Raccoon.DnsRelay.Diagnostics;
using Raccoon.DnsRelay.Resolving;
using Raccoon.DnsRelay.Server;

using Serilog;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder(args);

// Service
builder.Services
    .AddWindowsService(static options => options.ServiceName = "Raccoon.DnsRelay")
    .AddSystemd();

// Logging
builder.Logging.ClearProviders();
builder.Services.AddSerilog(options =>
{
    options.ReadFrom.Configuration(builder.Configuration);
});

// Options
builder.Services.AddOptions<ServerOptions>()
    .Bind(builder.Configuration.GetSection(ServerOptions.SectionName))
    .Validate(static o => IPAddress.TryParse(o.Address, out _), "Server:Address must be a valid IP address.")
    .Validate(static o => o.Port is >= 1 and <= 65535, "Server:Port must be between 1 and 65535.")
    .Validate(static o => o.ReceiveBufferSize is >= 512 and <= 65535, "Server:ReceiveBufferSize must be between 512 and 65535.")
    .Validate(static o => o.MaxConcurrentQueries >= 1, "Server:MaxConcurrentQueries must be at least 1.")
    .ValidateOnStart();

builder.Services.AddOptions<UpstreamOptions>()
    .Bind(builder.Configuration.GetSection(UpstreamOptions.SectionName))
    .Validate(static o => o.Servers is { Length: > 0 } && Array.TrueForAll(o.Servers, static s => IPAddress.TryParse(s, out _)), "Upstream:Servers must contain at least one valid IP address.")
    .Validate(static o => o.Port is >= 1 and <= 65535, "Upstream:Port must be between 1 and 65535.")
    .Validate(static o => o.TimeoutMs is >= 1 and <= 60000, "Upstream:TimeoutMs must be between 1 and 60000.")
    .ValidateOnStart();

builder.Services.AddOptions<CacheOptions>()
    .Bind(builder.Configuration.GetSection(CacheOptions.SectionName))
    .Validate(static o => o.MaxEntries >= 1, "Cache:MaxEntries must be at least 1.")
    .Validate(static o => o.MinTtlSeconds >= 0, "Cache:MinTtlSeconds must be non-negative.")
    .Validate(static o => o.MaxTtlSeconds >= o.MinTtlSeconds, "Cache:MaxTtlSeconds must be greater than or equal to MinTtlSeconds.")
    .Validate(static o => o.NegativeTtlSeconds >= 0, "Cache:NegativeTtlSeconds must be non-negative.")
    .Validate(static o => o.CleanupIntervalSeconds >= 1, "Cache:CleanupIntervalSeconds must be at least 1.")
    .ValidateOnStart();

builder.Services.AddOptions<TelemetryOptions>()
    .Bind(builder.Configuration.GetSection(TelemetryOptions.SectionName))
    .Validate(static o => !o.Enabled || string.IsNullOrEmpty(o.Otlp.Endpoint) || Uri.TryCreate(o.Otlp.Endpoint, UriKind.Absolute, out _), "Telemetry:Otlp:Endpoint must be a valid absolute URI.")
    .Validate(static o => !o.Enabled || !o.Prometheus.Enabled || (o.Prometheus.Port is >= 1 and <= 65535), "Telemetry:Prometheus:Port must be between 1 and 65535.")
    .ValidateOnStart();

// Services
builder.Services.AddSingleton<DnsRelayMetrics>();
builder.Services.AddSingleton<IDnsListener, UdpDnsListener>();
builder.Services.AddSingleton<UpstreamDnsResolver>();

var cacheOptions = builder.Configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();
if (cacheOptions.Enabled)
{
    builder.Services.AddSingleton<IDnsCache, DnsCache>();
    builder.Services.AddSingleton<IDnsResolver>(static provider => new CachingDnsResolver(
        provider.GetRequiredService<UpstreamDnsResolver>(),
        provider.GetRequiredService<IDnsCache>(),
        provider.GetRequiredService<IOptions<CacheOptions>>(),
        provider.GetRequiredService<DnsRelayMetrics>(),
        provider.GetRequiredService<ILogger<CachingDnsResolver>>()));
}
else
{
    builder.Services.AddSingleton<IDnsResolver>(static provider => provider.GetRequiredService<UpstreamDnsResolver>());
}

builder.Services.AddHostedService<DnsRelayService>();

// Telemetry (OpenTelemetry, enabled via configuration)
var telemetryOptions = builder.Configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new TelemetryOptions();
builder.Services.AddDnsRelayTelemetry(telemetryOptions);

// Build
var host = builder.Build();

var log = host.Services.GetRequiredService<ILogger<Program>>();

// Startup information
ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
log.InfoServiceStart();
log.InfoServiceEnvironment(typeof(Program).Assembly.GetName().Version, Environment.Version, Environment.CurrentDirectory);
log.InfoServiceGC(GCSettings.IsServerGC, GCSettings.LatencyMode, GCSettings.LargeObjectHeapCompactionMode);
log.InfoServiceThreadPool(workerThreads, completionPortThreads);

// Run
await host.RunAsync();
