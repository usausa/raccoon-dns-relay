namespace Raccoon.DnsRelay.Resolving;

internal interface IDnsResolver
{
    ValueTask<DnsResult> ResolveAsync(DnsQuery query, CancellationToken cancellationToken);
}
