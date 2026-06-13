namespace Raccoon.DnsRelay.Resolver;

using Raccoon.DnsRelay.Helpers;

// Owns the response bytes produced by a resolver. The listener sends
// Memory and then disposes the result exactly once
internal readonly struct DnsResult : IDisposable
{
    private readonly RentedBuffer buffer;

    public DnsResult(RentedBuffer buffer, int length)
    {
        this.buffer = buffer;
        Length = length;
    }

    public int Length { get; }

    public bool Success => Length > 0;

    public ReadOnlyMemory<byte> Memory => buffer.AsMemory(Length);

    public static DnsResult Empty => default;

    public void Dispose() => buffer.Dispose();
}
