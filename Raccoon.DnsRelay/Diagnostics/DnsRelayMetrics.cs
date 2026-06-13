namespace Raccoon.DnsRelay.Diagnostics;

using System.Diagnostics.Metrics;

/// <summary>
/// Holds the relay's instruments. The <see cref="Meter"/> is always present; when no
/// OpenTelemetry listener is attached the record calls are cheap no-ops.
/// </summary>
internal sealed class DnsRelayMetrics : IDisposable
{
    public const string MeterName = "Raccoon.DnsRelay";

    private readonly Meter meter;
    private readonly Counter<long> queries;
    private readonly Counter<long> cacheHits;
    private readonly Counter<long> cacheMisses;
    private readonly Counter<long> upstreamRequests;
    private readonly Counter<long> upstreamFailures;
    private readonly Counter<long> droppedQueries;
    private readonly UpDownCounter<long> activeQueries;
    private readonly Histogram<double> duration;

    public DnsRelayMetrics()
    {
        meter = new Meter(MeterName);
        queries = meter.CreateCounter<long>("dns.relay.queries", "{query}", "DNS queries received.");
        cacheHits = meter.CreateCounter<long>("dns.relay.cache.hits", "{hit}", "Cache hits.");
        cacheMisses = meter.CreateCounter<long>("dns.relay.cache.misses", "{miss}", "Cache misses.");
        upstreamRequests = meter.CreateCounter<long>("dns.relay.upstream.requests", "{request}", "Upstream requests.");
        upstreamFailures = meter.CreateCounter<long>("dns.relay.upstream.failures", "{failure}", "Upstream failures.");
        droppedQueries = meter.CreateCounter<long>("dns.relay.dropped", "{query}", "Dropped queries.");
        activeQueries = meter.CreateUpDownCounter<long>("dns.relay.active", "{query}", "In-flight queries.");
        duration = meter.CreateHistogram<double>("dns.relay.duration", "ms", "Query handling duration.");
    }

    public void QueryReceived() => queries.Add(1);

    public void CacheHit() => cacheHits.Add(1);

    public void CacheMiss() => cacheMisses.Add(1);

    public void UpstreamRequest(string upstream) =>
        upstreamRequests.Add(1, new KeyValuePair<string, object?>("upstream", upstream));

    public void UpstreamFailure(string upstream, string reason) =>
        upstreamFailures.Add(1, new KeyValuePair<string, object?>("upstream", upstream), new KeyValuePair<string, object?>("reason", reason));

    public void QueryDropped(string reason) =>
        droppedQueries.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void IncrementActive() => activeQueries.Add(1);

    public void DecrementActive() => activeQueries.Add(-1);

    public void RecordDuration(double milliseconds, string result) =>
        duration.Record(milliseconds, new KeyValuePair<string, object?>("result", result));

    public void Dispose() => meter.Dispose();
}
