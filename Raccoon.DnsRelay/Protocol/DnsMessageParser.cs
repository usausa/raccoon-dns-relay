namespace Raccoon.DnsRelay.Protocol;

using System.Buffers.Binary;
using System.Text;

// Minimal, allocation-free DNS message reading. Only the fields a relay needs
// (header, first question and the minimum TTL) are parsed
internal static class DnsMessageParser
{
    private const int OptRecordType = 41;

    public static bool TryReadHeader(ReadOnlySpan<byte> message, out DnsHeader header) =>
        DnsHeader.TryRead(message, out header);

    public static bool TryReadQuestion(ReadOnlySpan<byte> message, out DnsQuestion question)
    {
        question = default;

        var offset = DnsHeader.Length;
        if (message.Length < offset)
        {
            return false;
        }

        var nameStart = offset;
        while (true)
        {
            if (offset >= message.Length)
            {
                return false;
            }

            var labelLength = message[offset];
            if ((labelLength & 0xC0) != 0)
            {
                // A question name must use plain labels (0-63 bytes); reject pointers and reserved bits.
                return false;
            }

            offset++;
            if (labelLength == 0)
            {
                break;
            }

            offset += labelLength;
        }

        var nameLength = offset - nameStart;
        if ((offset + 4) > message.Length)
        {
            return false;
        }

        var type = BinaryPrimitives.ReadUInt16BigEndian(message[offset..]);
        var @class = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 2)..]);
        question = new DnsQuestion(nameStart, nameLength, type, @class);
        return true;
    }

    // Returns the smallest record TTL in the message (excluding the EDNS OPT record),
    // or 0 when there is no usable TTL
    public static uint ExtractMinTtl(ReadOnlySpan<byte> message)
    {
        if (!DnsHeader.TryRead(message, out var header))
        {
            return 0;
        }

        var offset = DnsHeader.Length;
        for (var i = 0; i < header.QuestionCount; i++)
        {
            if (!TrySkipName(message, ref offset))
            {
                return 0;
            }

            offset += 4;
        }

        var records = header.AnswerCount + header.AuthorityCount + header.AdditionalCount;
        var min = uint.MaxValue;
        for (var i = 0; i < records; i++)
        {
            if (!TrySkipName(message, ref offset) || ((offset + 10) > message.Length))
            {
                break;
            }

            var type = BinaryPrimitives.ReadUInt16BigEndian(message[offset..]);
            var ttl = BinaryPrimitives.ReadUInt32BigEndian(message[(offset + 4)..]);
            var rdLength = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 8)..]);
            offset += 10 + rdLength;

            if ((type != OptRecordType) && (ttl < min))
            {
                min = ttl;
            }
        }

        return min == uint.MaxValue ? 0 : min;
    }

    // Advances offset past a (possibly compressed) domain name
    public static bool TrySkipName(ReadOnlySpan<byte> message, ref int offset)
    {
        while (offset < message.Length)
        {
            var labelLength = message[offset];
            if (labelLength == 0)
            {
                offset++;
                return true;
            }

            if ((labelLength & 0xC0) == 0xC0)
            {
                // Compression pointer: two bytes and the name ends here.
                offset += 2;
                return offset <= message.Length;
            }

            offset += labelLength + 1;
        }

        return false;
    }

    // Decodes a question name into a dotted string. Used only for diagnostic logging
    public static string ReadName(ReadOnlySpan<byte> message, int nameOffset)
    {
        var builder = new StringBuilder(64);
        var offset = nameOffset;
        while (offset < message.Length)
        {
            var labelLength = message[offset];
            if ((labelLength == 0) || ((labelLength & 0xC0) != 0))
            {
                break;
            }

            offset++;
            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            for (var i = 0; (i < labelLength) && (offset < message.Length); i++)
            {
                builder.Append((char)message[offset]);
                offset++;
            }
        }

        return builder.Length == 0 ? "." : builder.ToString();
    }

    // Maps a question QTYPE to its mnemonic for diagnostic logging, falling back to TYPE<n>
    public static string TypeToText(ushort type) => type switch
    {
        1 => "A",
        2 => "NS",
        5 => "CNAME",
        6 => "SOA",
        12 => "PTR",
        15 => "MX",
        16 => "TXT",
        28 => "AAAA",
        33 => "SRV",
        65 => "HTTPS",
        255 => "ANY",
        _ => string.Create(CultureInfo.InvariantCulture, $"TYPE{type}")
    };
}
