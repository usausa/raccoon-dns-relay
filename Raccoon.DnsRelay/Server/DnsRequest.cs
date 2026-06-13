namespace Raccoon.DnsRelay.Server;

internal readonly struct DnsRequest
{
    public DnsRequest(ReadOnlyMemory<byte> query)
    {
        Query = query;
    }

    public ReadOnlyMemory<byte> Query { get; }
}
