namespace Raccoon.DnsRelay.Protocol;

// The first question of a DNS message, kept as offsets into the owning message
// so the wire-format name can be hashed for the cache key without allocating a string
internal readonly struct DnsQuestion
{
    public DnsQuestion(int nameOffset, int nameLength, ushort type, ushort @class)
    {
        NameOffset = nameOffset;
        NameLength = nameLength;
        Type = type;
        Class = @class;
    }

    public int NameOffset { get; }

    public int NameLength { get; }

    public ushort Type { get; }

    public ushort Class { get; }
}
