namespace Raccoon.DnsRelay.Caching;

// Cache key built from a question's wire bytes (QNAME + QTYPE + QCLASS).
// The name portion is ASCII-lowercased (DNS names are case-insensitive); the
// trailing 4 type/class bytes are kept exact. A 64-bit FNV-1a hash is precomputed
internal readonly struct QueryKey : IEquatable<QueryKey>
{
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    private readonly byte[] question;
    private readonly ulong hash;

    private QueryKey(byte[] question, ulong hash)
    {
        this.question = question;
        this.hash = hash;
    }

    public ReadOnlySpan<byte> Question => question;

    public ulong Hash => hash;

    public static QueryKey From(ReadOnlySpan<byte> question)
    {
        var normalized = new byte[question.Length];
        var nameEnd = question.Length - 4;
        for (var i = 0; i < question.Length; i++)
        {
            normalized[i] = (i < nameEnd) ? ToLower(question[i]) : question[i];
        }

        return new QueryKey(normalized, ComputeHash(question));
    }

    public static ulong ComputeHash(ReadOnlySpan<byte> question)
    {
        var hash = FnvOffsetBasis;
        var nameEnd = question.Length - 4;
        for (var i = 0; i < question.Length; i++)
        {
            var b = (i < nameEnd) ? ToLower(question[i]) : question[i];
            hash = (hash ^ b) * FnvPrime;
        }

        return hash;
    }

    public static byte ToLower(byte b) => (byte)(((b >= 0x41) && (b <= 0x5A)) ? (b + 0x20) : b);

    public bool Equals(QueryKey other) => (hash == other.hash) && question.AsSpan().SequenceEqual(other.question);

    public override bool Equals(object? obj) => obj is QueryKey other && Equals(other);

    public override int GetHashCode() => (int)hash;
}
