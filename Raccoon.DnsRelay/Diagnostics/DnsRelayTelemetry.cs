namespace Raccoon.DnsRelay.Diagnostics;

internal static class DnsRelayTelemetry
{
    public const string ActivitySourceName = "Raccoon.DnsRelay";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
