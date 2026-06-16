namespace Raccoon.DnsRelay.Server;

internal readonly struct DnsRequest
{
    public DnsRequest(ReadOnlyMemory<byte> query, IPEndPoint client)
    {
        Query = query;
        Client = client;
    }

    public ReadOnlyMemory<byte> Query { get; }

    // Remote endpoint of the client that sent the query (used for query logging).
    public IPEndPoint Client { get; }
}
