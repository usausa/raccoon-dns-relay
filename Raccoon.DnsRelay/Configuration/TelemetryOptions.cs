namespace Raccoon.DnsRelay.Configuration;

internal sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    public bool Enabled { get; set; }

    public string ServiceName { get; set; } = "Raccoon.DnsRelay";

    public bool EnableMetrics { get; set; } = true;

    public bool EnableTracing { get; set; } = true;

    public bool EnableRuntimeInstrumentation { get; set; }

    public OtlpOptions Otlp { get; set; } = new();

    public PrometheusOptions Prometheus { get; set; } = new();

    public bool EnableConsoleExporter { get; set; }

    internal sealed class OtlpOptions
    {
        public string? Endpoint { get; set; }

        public string Protocol { get; set; } = "Grpc";
    }

    internal sealed class PrometheusOptions
    {
        public bool Enabled { get; set; }

        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 9464;

        public string ScrapeEndpointPath { get; set; } = "/metrics";
    }
}
