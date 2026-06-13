namespace Raccoon.DnsRelay.Resolving;

using Raccoon.DnsRelay.Buffers;

/// <summary>
/// Owns the response bytes produced by a resolver. The listener sends
/// <see cref="Memory"/> and then disposes the result exactly once.
/// </summary>
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
