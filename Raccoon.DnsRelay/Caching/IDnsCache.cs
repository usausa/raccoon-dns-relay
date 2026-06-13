namespace Raccoon.DnsRelay.Caching;

internal interface IDnsCache
{
    bool TryGet(ReadOnlySpan<byte> question, out CacheEntry entry);

    void Set(ReadOnlySpan<byte> question, ReadOnlySpan<byte> response, long expiresAtTicks, uint originalTtl);
}
