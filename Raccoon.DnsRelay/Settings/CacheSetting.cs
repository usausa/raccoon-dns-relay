namespace Raccoon.DnsRelay.Settings;

internal sealed class CacheSetting
{
    public const string SectionName = "Cache";

    public bool Enabled { get; set; } = true;

    public int MaxEntries { get; set; } = 10000;

    public int MinTtlSeconds { get; set; } = 5;

    public int MaxTtlSeconds { get; set; } = 86400;

    public int NegativeTtlSeconds { get; set; }

    public bool DecrementTtl { get; set; } = true;

    public int CleanupIntervalSeconds { get; set; } = 60;
}
