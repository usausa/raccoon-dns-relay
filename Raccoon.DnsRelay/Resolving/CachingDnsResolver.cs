namespace Raccoon.DnsRelay.Resolving;

using System.Buffers.Binary;

using Microsoft.Extensions.Options;

using Raccoon.DnsRelay.Buffers;
using Raccoon.DnsRelay.Caching;
using Raccoon.DnsRelay.Configuration;
using Raccoon.DnsRelay.Diagnostics;
using Raccoon.DnsRelay.Protocol;

/// <summary>
/// Decorator that serves responses from <see cref="IDnsCache"/> when possible and
/// stores successful (and optionally negative) responses from the inner resolver.
/// </summary>
internal sealed class CachingDnsResolver : IDnsResolver
{
    private const int NoError = 0;
    private const int NxDomain = 3;

    private readonly IDnsResolver inner;
    private readonly IDnsCache cache;
    private readonly CacheOptions options;
    private readonly DnsRelayMetrics metrics;
    private readonly ILogger<CachingDnsResolver> log;

    public CachingDnsResolver(IDnsResolver inner, IDnsCache cache, IOptions<CacheOptions> options, DnsRelayMetrics metrics, ILogger<CachingDnsResolver> log)
    {
        this.inner = inner;
        this.cache = cache;
        this.options = options.Value;
        this.metrics = metrics;
        this.log = log;
    }

    public async ValueTask<DnsResult> ResolveAsync(DnsQuery query, CancellationToken cancellationToken)
    {
        if (cache.TryGet(query.QuestionSpan, out var entry))
        {
            metrics.CacheHit();
            log.DebugCacheHit(query.TransactionId);
            return BuildFromCache(query, entry);
        }

        metrics.CacheMiss();
        var result = await inner.ResolveAsync(query, cancellationToken);
        if (result.Success)
        {
            Store(query, result);
        }

        return result;
    }

    private DnsResult BuildFromCache(DnsQuery query, CacheEntry entry)
    {
        var length = entry.Response.Length;
        var buffer = RentedBuffer.Rent(length);
        var span = buffer.Span[..length];
        entry.Response.CopyTo(span);

        // Rewrite the transaction id to match the client's query.
        BinaryPrimitives.WriteUInt16BigEndian(span, query.TransactionId);

        if (options.DecrementTtl)
        {
            var remainingMs = entry.ExpiresAtTicks - Environment.TickCount64;
            var remaining = remainingMs > 0 ? (uint)(remainingMs / 1000) : 0;
            var elapsed = entry.OriginalTtl > remaining ? entry.OriginalTtl - remaining : 0;
            if (elapsed > 0)
            {
                DnsResponseFactory.DecrementTtls(span, elapsed);
            }
        }

        return new DnsResult(buffer, length);
    }

    private void Store(DnsQuery query, DnsResult result)
    {
        var response = result.Memory.Span;
        if (!DnsMessageParser.TryReadHeader(response, out var header))
        {
            return;
        }

        long ttlSeconds;
        if ((header.ResponseCode == NoError) && (header.AnswerCount > 0))
        {
            var minTtl = DnsMessageParser.ExtractMinTtl(response);
            if (minTtl == 0)
            {
                return;
            }

            ttlSeconds = Math.Clamp(minTtl, (uint)options.MinTtlSeconds, (uint)options.MaxTtlSeconds);
        }
        else if (IsNegative(header) && (options.NegativeTtlSeconds > 0))
        {
            ttlSeconds = options.NegativeTtlSeconds;
        }
        else
        {
            return;
        }

        var expiresAt = Environment.TickCount64 + (ttlSeconds * 1000);
        cache.Set(query.QuestionSpan, response, expiresAt, (uint)ttlSeconds);
    }

    private static bool IsNegative(DnsHeader header) =>
        (header.ResponseCode == NxDomain) || ((header.ResponseCode == NoError) && (header.AnswerCount == 0));
}
