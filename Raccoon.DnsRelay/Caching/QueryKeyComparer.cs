namespace Raccoon.DnsRelay.Caching;

/// <summary>
/// Equality comparer for <see cref="QueryKey"/> that also supports looking up entries
/// directly from a raw <see cref="ReadOnlySpan{T}"/> question slice, so a cache hit does
/// not allocate a key.
/// </summary>
internal sealed class QueryKeyComparer :
    IEqualityComparer<QueryKey>,
    IAlternateEqualityComparer<ReadOnlySpan<byte>, QueryKey>
{
    public bool Equals(QueryKey x, QueryKey y) => x.Equals(y);

    public int GetHashCode(QueryKey obj) => (int)obj.Hash;

    public bool Equals(ReadOnlySpan<byte> alternate, QueryKey other)
    {
        var stored = other.Question;
        if (alternate.Length != stored.Length)
        {
            return false;
        }

        var nameEnd = alternate.Length - 4;
        for (var i = 0; i < alternate.Length; i++)
        {
            var b = (i < nameEnd) ? QueryKey.ToLower(alternate[i]) : alternate[i];
            if (b != stored[i])
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(ReadOnlySpan<byte> alternate) => (int)QueryKey.ComputeHash(alternate);

    public QueryKey Create(ReadOnlySpan<byte> alternate) => QueryKey.From(alternate);
}
