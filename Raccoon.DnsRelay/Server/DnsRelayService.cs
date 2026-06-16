namespace Raccoon.DnsRelay.Server;

using Raccoon.DnsRelay.Helpers;
using Raccoon.DnsRelay.Protocol;
using Raccoon.DnsRelay.Resolver;
using Raccoon.DnsRelay.Telemetry;

internal sealed class DnsRelayService : BackgroundService
{
    private readonly IDnsListener listener;
    private readonly IDnsResolver resolver;
    private readonly DnsRelayMetrics metrics;
    private readonly ILogger<DnsRelayService> log;
    private readonly ILogger queryLog;

    public DnsRelayService(IDnsListener listener, IDnsResolver resolver, DnsRelayMetrics metrics, ILoggerFactory loggerFactory)
    {
        this.listener = listener;
        this.resolver = resolver;
        this.metrics = metrics;
        log = loggerFactory.CreateLogger<DnsRelayService>();
        queryLog = loggerFactory.CreateLogger("Raccoon.DnsRelay.Query");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return listener.RunAsync(HandleQueryAsync, stoppingToken);
    }

    private async ValueTask<DnsResult> HandleQueryAsync(DnsRequest request, CancellationToken cancellationToken)
    {
        metrics.QueryReceived();
        metrics.IncrementActive();
        var startTimestamp = Stopwatch.GetTimestamp();
        var resultLabel = "invalid";
        // ReSharper disable once ExplicitCallerInfoArgument
        using var activity = DnsRelayTelemetry.ActivitySource.StartActivity("dns.relay.query");
        try
        {
            DnsQuery query;
            string? name = null;

            // Parse synchronously so the span does not cross an await.
            {
                var message = request.Query.Span;
                if (!DnsMessageParser.TryReadHeader(message, out var header) ||
                    (header.QuestionCount == 0) ||
                    !DnsMessageParser.TryReadQuestion(message, out var question))
                {
                    log.DebugInvalidQuery();
                    return DnsResult.Empty;
                }

                if (log.IsEnabled(LogLevel.Debug))
                {
                    name = DnsMessageParser.ReadName(message, question.NameOffset);
                    log.DebugQueryReceived(header.Id, name, question.Type);
                }

                activity?.SetTag("dns.transaction_id", (int)header.Id);
                activity?.SetTag("dns.question_type", (int)question.Type);
                query = new DnsQuery(request.Query, header.Id, question);
            }

            var result = await resolver.ResolveAsync(query, cancellationToken);

            DnsResult response;
            if (result.Success)
            {
                resultLabel = "resolved";
                response = result;
            }
            else
            {
                // Every upstream failed: synthesize a SERVFAIL so the client is not left waiting.
                resultLabel = "servfail";
                var questionEnd = query.Question.NameOffset + query.Question.NameLength + 4;
                var buffer = RentedBuffer.Rent(questionEnd);
                var length = DnsResponseFactory.WriteServerFailure(request.Query.Span, questionEnd, buffer.Span);
                response = new DnsResult(buffer, length);
            }

            // Record which client resolved which name (always emitted; see the "Raccoon.DnsRelay.Query" level override).
            if (queryLog.IsEnabled(LogLevel.Information))
            {
                name ??= DnsMessageParser.ReadName(query.RawMessage.Span, query.Question.NameOffset);
                queryLog.InfoQueryResolved(request.Client.Address.ToString(), name, DnsMessageParser.TypeToText(query.Question.Type), resultLabel);
            }

            return response;
        }
        finally
        {
            metrics.DecrementActive();
            metrics.RecordDuration(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, resultLabel);
            activity?.SetTag("dns.result", resultLabel);
        }
    }
}
