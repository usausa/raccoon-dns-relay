namespace Raccoon.DnsRelay.Protocol;

using System.Buffers.Binary;

internal static class DnsResponseFactory
{
    private const int OptRecordType = 41;

    /// <summary>
    /// Writes a minimal SERVFAIL response (header + the original question) into
    /// <paramref name="destination"/> and returns the number of bytes written.
    /// </summary>
    public static int WriteServerFailure(ReadOnlySpan<byte> query, int questionEnd, Span<byte> destination)
    {
        query[..questionEnd].CopyTo(destination);

        // QR = 1 (response); keep OPCODE and RD from the query.
        destination[2] = (byte)(query[2] | 0x80);
        // RA = 0, Z = 0, RCODE = 2 (SERVFAIL).
        destination[3] = 0x02;
        // No answer/authority/additional records.
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(destination[8..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(destination[10..], 0);

        return questionEnd;
    }

    /// <summary>
    /// Subtracts <paramref name="elapsedSeconds"/> from every record TTL in place
    /// (clamped at 0), so a cached response reflects the remaining lifetime.
    /// The EDNS OPT record is left untouched.
    /// </summary>
    public static void DecrementTtls(Span<byte> message, uint elapsedSeconds)
    {
        if (!DnsHeader.TryRead(message, out var header))
        {
            return;
        }

        var offset = DnsHeader.Length;
        for (var i = 0; i < header.QuestionCount; i++)
        {
            if (!DnsMessageParser.TrySkipName(message, ref offset))
            {
                return;
            }

            offset += 4;
        }

        var records = header.AnswerCount + header.AuthorityCount + header.AdditionalCount;
        for (var i = 0; i < records; i++)
        {
            if (!DnsMessageParser.TrySkipName(message, ref offset) || ((offset + 10) > message.Length))
            {
                return;
            }

            var type = BinaryPrimitives.ReadUInt16BigEndian(message[offset..]);
            if (type != OptRecordType)
            {
                var ttlSpan = message.Slice(offset + 4, 4);
                var ttl = BinaryPrimitives.ReadUInt32BigEndian(ttlSpan);
                BinaryPrimitives.WriteUInt32BigEndian(ttlSpan, ttl > elapsedSeconds ? ttl - elapsedSeconds : 0);
            }

            var rdLength = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 8)..]);
            offset += 10 + rdLength;
        }
    }
}
