namespace Raccoon.DnsRelay.Caching;

using System.Collections.Concurrent;

using Microsoft.Extensions.Options;

using Raccoon.DnsRelay.Settings;

// TTL cache keyed by the question wire bytes. Lookups use an alternate
// ReadOnlySpan<byte> lookup (no key allocation), expiry is lazy
// (checked on read), and bounding is done by a periodic sweep plus approximate
// eviction without sorting
internal sealed class DnsCache : IDnsCache, IDisposable
{
    private readonly ConcurrentDictionary<QueryKey, CacheEntry> entries;
    private readonly int maxEntries;
    private readonly Timer cleanupTimer;

    public DnsCache(IOptions<CacheSetting> options)
    {
        var value = options.Value;
        maxEntries = value.MaxEntries;
        entries = new ConcurrentDictionary<QueryKey, CacheEntry>(new QueryKeyComparer());

        var interval = TimeSpan.FromSeconds(value.CleanupIntervalSeconds);
        cleanupTimer = new Timer(static state => ((DnsCache)state!).Cleanup(), this, interval, interval);
    }

    public bool TryGet(ReadOnlySpan<byte> question, out CacheEntry entry)
    {
        var lookup = entries.GetAlternateLookup<ReadOnlySpan<byte>>();
        if (lookup.TryGetValue(question, out entry))
        {
            if (entry.ExpiresAtTicks > Environment.TickCount64)
            {
                return true;
            }

            lookup.TryRemove(question, out _);
        }

        entry = default;
        return false;
    }

    public void Set(ReadOnlySpan<byte> question, ReadOnlySpan<byte> response, long expiresAtTicks, uint originalTtl)
    {
        if (entries.Count >= maxEntries)
        {
            Evict();
        }

        entries[QueryKey.From(question)] = new CacheEntry(response.ToArray(), expiresAtTicks, originalTtl);
    }

    private void Evict()
    {
        var now = Environment.TickCount64;

        // First drop expired entries (lazy cleanup also keeps this cheap).
        foreach (var pair in entries)
        {
            if (pair.Value.ExpiresAtTicks <= now)
            {
                entries.TryRemove(pair.Key, out _);
            }
        }

        // If still at capacity, drop arbitrary entries down to a low-water mark (no sorting).
        if (entries.Count >= maxEntries)
        {
            var lowWater = (maxEntries * 9) / 10;
            foreach (var pair in entries)
            {
                if (entries.Count <= lowWater)
                {
                    break;
                }

                entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private void Cleanup()
    {
        var now = Environment.TickCount64;
        foreach (var pair in entries)
        {
            if (pair.Value.ExpiresAtTicks <= now)
            {
                entries.TryRemove(pair.Key, out _);
            }
        }
    }

    public void Dispose() => cleanupTimer.Dispose();
}
