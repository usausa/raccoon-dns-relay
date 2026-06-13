namespace Raccoon.DnsRelay.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Raccoon.DnsRelay.Settings;

internal static class OpenTelemetryExtensions
{
    public static IServiceCollection AddDnsRelayTelemetry(this IServiceCollection services, TelemetrySetting options)
    {
        if (!options.Enabled)
        {
            return services;
        }

        var builder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName));

        if (options.EnableMetrics)
        {
            builder.WithMetrics(metrics =>
            {
                metrics.AddMeter(DnsRelayMetrics.MeterName);
                if (options.EnableRuntimeInstrumentation)
                {
                    metrics.AddRuntimeInstrumentation();
                }

                if (options.EnableConsoleExporter)
                {
                    metrics.AddConsoleExporter();
                }

                if (!string.IsNullOrEmpty(options.Otlp.Endpoint))
                {
                    metrics.AddOtlpExporter(exporter => ConfigureOtlp(exporter, options.Otlp));
                }

                if (options.Prometheus.Enabled)
                {
                    metrics.AddPrometheusHttpListener(listener =>
                    {
                        listener.Host = options.Prometheus.Host;
                        listener.Port = options.Prometheus.Port;
                        listener.ScrapeEndpointPath = options.Prometheus.ScrapeEndpointPath;
                    });
                }
            });
        }

        if (options.EnableTracing)
        {
            builder.WithTracing(tracing =>
            {
                tracing.AddSource(DnsRelayTelemetry.ActivitySourceName);
                if (options.EnableConsoleExporter)
                {
                    tracing.AddConsoleExporter();
                }

                if (!string.IsNullOrEmpty(options.Otlp.Endpoint))
                {
                    tracing.AddOtlpExporter(exporter => ConfigureOtlp(exporter, options.Otlp));
                }
            });
        }

        return services;
    }

    private static void ConfigureOtlp(OtlpExporterOptions exporter, TelemetrySetting.OtlpSetting otlp)
    {
        exporter.Endpoint = new Uri(otlp.Endpoint!);
        exporter.Protocol = string.Equals(otlp.Protocol, "HttpProtobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
    }
}
