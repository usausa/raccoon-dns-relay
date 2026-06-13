namespace Raccoon.DnsRelay.Resolver;

internal interface IDnsResolver
{
    ValueTask<DnsResult> ResolveAsync(DnsQuery query, CancellationToken cancellationToken);
}
