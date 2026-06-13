# Raccoon.DnsRelay

A service that relays client DNS queries (UDP) to upstream DNS servers.
It runs as a Windows service / systemd daemon, with a TTL cache and OpenTelemetry-based observability.

- .NET 10 / Worker service
- UDP relay with upstream failover
- Low-allocation design (`Span`, `ArrayPool`, raw `Socket`)
- Efficient TTL cache (allocation-free lookup, TTL decrement, GC-friendly)
- OpenTelemetry (metrics / traces, enabled via configuration)

## Requirements

- .NET 10 SDK / runtime

## Build

```pwsh
dotnet build Raccoon.DnsRelay/Raccoon.DnsRelay.csproj -c Release
```

## Run

```pwsh
# Run with the default settings (appsettings.json)
dotnet run --project Raccoon.DnsRelay

# Development (appsettings.Development.json: 127.0.0.1:15353)
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project Raccoon.DnsRelay
```

> **Port 53 requires elevated privileges** (root / `CAP_NET_BIND_SERVICE` on Linux).
> On Windows the built-in mDNS service occupies port 5353, so the development default is **15353**.

Quick check (in another terminal):

```pwsh
nslookup -port=15353 example.com 127.0.0.1
```

## Configuration (appsettings.json)

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| `Server` | `Address` / `Port` | `0.0.0.0` / `53` | Listen address / port |
| | `ReceiveBufferSize` | `4096` | Receive buffer (EDNS0) |
| | `MaxConcurrentQueries` | `1024` | Concurrency limit (excess is dropped) |
| `Upstream` | `Servers` | `["8.8.8.8","1.1.1.1"]` | Upstream DNS servers (failover in order) |
| | `Port` / `TimeoutMs` | `53` / `3000` | Upstream port / timeout |
| `Cache` | `Enabled` | `true` | Enable the cache |
| | `MaxEntries` | `10000` | Maximum number of entries |
| | `MinTtlSeconds` / `MaxTtlSeconds` | `5` / `86400` | TTL clamp range |
| | `NegativeTtlSeconds` | `0` | Negative-cache TTL (0 disables) |
| | `DecrementTtl` | `true` | Rewrite the response TTL to the remaining seconds on a hit |
| | `CleanupIntervalSeconds` | `60` | Expiry sweep interval |
| `Telemetry` | `Enabled` | `false` | Enable OpenTelemetry (master switch) |
| | `ServiceName` | `Raccoon.DnsRelay` | Resource service name |
| | `EnableMetrics` / `EnableTracing` | `true` / `true` | Metrics / traces |
| | `EnableRuntimeInstrumentation` | `false` | Runtime metrics |
| | `Otlp.Endpoint` / `Otlp.Protocol` | `""` / `Grpc` | OTLP push target (empty disables; `Grpc` / `HttpProtobuf`) |
| | `Prometheus.Enabled` | `false` | Expose a Prometheus pull endpoint |
| | `Prometheus.Host` / `Prometheus.Port` | `localhost` / `9464` | Scrape host / port |
| | `Prometheus.ScrapeEndpointPath` | `/metrics` | Scrape path |
| | `EnableConsoleExporter` | `false` | Console output |

Settings can also be overridden with environment variables or command-line arguments (e.g. `--Server:Port=15353`, `--Telemetry:Enabled=true`).
Logging is configured under the `Serilog` section (Console / File).

## Observability (OpenTelemetry)

Enabled with `Telemetry:Enabled=true` (master switch). When disabled, the instrumentation is a no-op with no overhead.

- Metrics (Meter `Raccoon.DnsRelay`): `dns.relay.queries` / `cache.hits` / `cache.misses` / `upstream.requests` / `upstream.failures` / `dropped` / `active` / `duration`
- Traces (ActivitySource `Raccoon.DnsRelay`): `dns.relay.query` (parent) / `dns.relay.upstream` (child)

Exporters can be selected independently:

- **OTLP push**: set `Otlp.Endpoint` (empty disables it).
- **Prometheus pull**: `Prometheus.Enabled=true`. Exposes `http://{Host}:{Port}{ScrapeEndpointPath}` (default `http://localhost:9464/metrics`) for Prometheus to scrape (an OTLP-free setup is supported).
- **Console**: `EnableConsoleExporter=true`.

```pwsh
# OTLP push (send to a collector)
dotnet run --project Raccoon.DnsRelay -- --Telemetry:Enabled=true --Telemetry:Otlp:Endpoint=http://localhost:4317

# Prometheus pull only (no OTLP)
dotnet run --project Raccoon.DnsRelay -- --Telemetry:Enabled=true --Telemetry:Prometheus:Enabled=true
```

Example Prometheus scrape config:

```yaml
scrape_configs:
  - job_name: raccoon-dnsrelay
    static_configs:
      - targets: ['localhost:9464']
```

## Windows service

```pwsh
# Publish
dotnet publish Raccoon.DnsRelay/Raccoon.DnsRelay.csproj -c Release -r win-x64 --self-contained false -o publish

# Install (elevated; the space after binPath= is required)
sc.exe create Raccoon.DnsRelay binPath= "C:\path\to\publish\Raccoon.DnsRelay.exe" start= auto
sc.exe start Raccoon.DnsRelay

# Uninstall
sc.exe stop Raccoon.DnsRelay
sc.exe delete Raccoon.DnsRelay
```

## systemd (Linux)

```ini
[Unit]
Description=Raccoon DNS Relay
After=network.target

[Service]
Type=notify
ExecStart=/opt/raccoon-dnsrelay/Raccoon.DnsRelay
Restart=on-failure
# To bind port 53 as a non-root user
AmbientCapabilities=CAP_NET_BIND_SERVICE

[Install]
WantedBy=multi-user.target
```

## License

MIT
