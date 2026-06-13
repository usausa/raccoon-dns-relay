namespace Raccoon.DnsRelay.Settings;

internal sealed class ServerSetting
{
    public const string SectionName = "Server";

    public string Address { get; set; } = "0.0.0.0";

    public int Port { get; set; } = 53;

    public int ReceiveBufferSize { get; set; } = 4096;

    public int MaxConcurrentQueries { get; set; } = 1024;
}
