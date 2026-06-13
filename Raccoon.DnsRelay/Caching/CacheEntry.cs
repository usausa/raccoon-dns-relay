namespace Raccoon.DnsRelay.Caching;

// A cached response. Stored once as a right-sized array (long-lived and stable),
// keeping per-request churn off the GC heap
internal readonly struct CacheEntry
{
    private readonly byte[]? response;

    public CacheEntry(byte[] response, long expiresAtTicks, uint originalTtl)
    {
        this.response = response;
        ExpiresAtTicks = expiresAtTicks;
        OriginalTtl = originalTtl;
    }

    public ReadOnlySpan<byte> Response => response;

    public long ExpiresAtTicks { get; }

    public uint OriginalTtl { get; }
}
