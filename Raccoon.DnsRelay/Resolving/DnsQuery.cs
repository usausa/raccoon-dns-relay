namespace Raccoon.DnsRelay.Resolving;

using Raccoon.DnsRelay.Protocol;

internal readonly struct DnsQuery
{
    public DnsQuery(ReadOnlyMemory<byte> rawMessage, ushort transactionId, DnsQuestion question)
    {
        RawMessage = rawMessage;
        TransactionId = transactionId;
        Question = question;
    }

    public ReadOnlyMemory<byte> RawMessage { get; }

    public ushort TransactionId { get; }

    public DnsQuestion Question { get; }

    /// <summary>
    /// The question wire slice (QNAME + QTYPE + QCLASS) used as the cache key.
    /// Recomputed on each access so it never crosses an await boundary.
    /// </summary>
    public ReadOnlySpan<byte> QuestionSpan => RawMessage.Span.Slice(Question.NameOffset, Question.NameLength + 4);
}
