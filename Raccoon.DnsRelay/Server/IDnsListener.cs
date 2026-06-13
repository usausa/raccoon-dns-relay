namespace Raccoon.DnsRelay.Server;

using Raccoon.DnsRelay.Resolving;

internal delegate ValueTask<DnsResult> DnsQueryHandler(DnsRequest request, CancellationToken cancellationToken);

internal interface IDnsListener : IAsyncDisposable
{
    Task RunAsync(DnsQueryHandler handler, CancellationToken cancellationToken);
}
