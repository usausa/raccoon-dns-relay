namespace Raccoon.DnsRelay.Buffers;

/// <summary>
/// Owns a buffer rented from <see cref="ArrayPool{T}"/> and returns it on dispose.
/// Keeps per-request allocations off the GC heap on the hot path.
/// </summary>
internal readonly struct RentedBuffer : IDisposable
{
    private readonly byte[]? array;
    private readonly int capacity;

    private RentedBuffer(byte[] array, int capacity)
    {
        this.array = array;
        this.capacity = capacity;
    }

    public int Capacity => capacity;

    public Span<byte> Span => array.AsSpan(0, capacity);

    public Memory<byte> Memory => array.AsMemory(0, capacity);

    public static RentedBuffer Rent(int minimumSize)
    {
        var rented = ArrayPool<byte>.Shared.Rent(minimumSize);
        return new RentedBuffer(rented, rented.Length);
    }

    public Memory<byte> AsMemory(int length) => array.AsMemory(0, length);

    public void Dispose()
    {
        if (array is not null)
        {
            ArrayPool<byte>.Shared.Return(array);
        }
    }
}
