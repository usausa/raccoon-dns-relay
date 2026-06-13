namespace Raccoon.DnsRelay.Protocol;

using System.Buffers.Binary;

// The fixed 12-byte DNS message header (RFC 1035 section 4.1.1)
internal readonly struct DnsHeader
{
    public const int Length = 12;

    public ushort Id { get; private init; }

    public ushort Flags { get; private init; }

    public ushort QuestionCount { get; private init; }

    public ushort AnswerCount { get; private init; }

    public ushort AuthorityCount { get; private init; }

    public ushort AdditionalCount { get; private init; }

    public bool IsResponse => (Flags & 0x8000) != 0;

    public int ResponseCode => Flags & 0x000F;

    public static bool TryRead(ReadOnlySpan<byte> message, out DnsHeader header)
    {
        if (message.Length < Length)
        {
            header = default;
            return false;
        }

        header = new DnsHeader
        {
            Id = BinaryPrimitives.ReadUInt16BigEndian(message),
            Flags = BinaryPrimitives.ReadUInt16BigEndian(message[2..]),
            QuestionCount = BinaryPrimitives.ReadUInt16BigEndian(message[4..]),
            AnswerCount = BinaryPrimitives.ReadUInt16BigEndian(message[6..]),
            AuthorityCount = BinaryPrimitives.ReadUInt16BigEndian(message[8..]),
            AdditionalCount = BinaryPrimitives.ReadUInt16BigEndian(message[10..])
        };
        return true;
    }
}
