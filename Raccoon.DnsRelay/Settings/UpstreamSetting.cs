namespace Raccoon.DnsRelay.Settings;

internal sealed class UpstreamSetting
{
    public const string SectionName = "Upstream";

    public string[] Servers { get; set; } = [];

    public int Port { get; set; } = 53;

    public int TimeoutMs { get; set; } = 3000;
}
