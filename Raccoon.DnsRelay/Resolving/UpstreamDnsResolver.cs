namespace Raccoon.DnsRelay.Resolving;

using Microsoft.Extensions.Options;

using Raccoon.DnsRelay.Buffers;
using Raccoon.DnsRelay.Configuration;
using Raccoon.DnsRelay.Diagnostics;
using Raccoon.DnsRelay.Protocol;

/// <summary>
/// Forwards the raw query to the configured upstream servers in order,
/// falling over to the next one on timeout or socket error.
/// </summary>
internal sealed class UpstreamDnsResolver : IDnsResolver
{
    private readonly IPEndPoint[] upstreams;
    private readonly string[] upstreamNames;
    private readonly int timeoutMs;
    private readonly int bufferSize;
    private readonly DnsRelayMetrics metrics;
    private readonly ILogger<UpstreamDnsResolver> log;

    public UpstreamDnsResolver(
        IOptions<UpstreamOptions> options,
        IOptions<ServerOptions> serverOptions,
        DnsRelayMetrics metrics,
        ILogger<UpstreamDnsResolver> log)
    {
        var value = options.Value;
        upstreams = Array.ConvertAll(value.Servers, server => new IPEndPoint(IPAddress.Parse(server), value.Port));
        upstreamNames = Array.ConvertAll(upstreams, static endpoint => endpoint.ToString());
        timeoutMs = value.TimeoutMs;
        bufferSize = serverOptions.Value.ReceiveBufferSize;
        this.metrics = metrics;
        this.log = log;
    }

    public async ValueTask<DnsResult> ResolveAsync(DnsQuery query, CancellationToken cancellationToken)
    {
        for (var i = 0; i < upstreams.Length; i++)
        {
            var endpoint = upstreams[i];
            var name = upstreamNames[i];

            using var activity = DnsRelayTelemetry.ActivitySource.StartActivity("dns.relay.upstream");
            activity?.SetTag("server.address", name);
            metrics.UpstreamRequest(name);

            try
            {
                using var socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                // Connecting a UDP socket sets the peer and filters out responses from other addresses.
                await socket.ConnectAsync(endpoint, cancellationToken);
                await socket.SendAsync(query.RawMessage, SocketFlags.None, cancellationToken);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(timeoutMs);

                var buffer = RentedBuffer.Rent(bufferSize);
                var transferred = false;
                try
                {
                    var received = await socket.ReceiveAsync(buffer.Memory, SocketFlags.None, timeout.Token);
                    if (received >= DnsHeader.Length)
                    {
                        log.DebugUpstreamResolved(endpoint, received);
                        transferred = true;
                        return new DnsResult(buffer, received);
                    }
                }
                finally
                {
                    if (!transferred)
                    {
                        buffer.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                metrics.UpstreamFailure(name, "timeout");
                activity?.SetStatus(ActivityStatusCode.Error, "timeout");
                log.WarnUpstreamTimeout(endpoint);
            }
            catch (SocketException ex)
            {
                metrics.UpstreamFailure(name, "socket");
                activity?.SetStatus(ActivityStatusCode.Error, "socket");
                log.WarnUpstreamSocketError(endpoint, ex);
            }
        }

        return DnsResult.Empty;
    }
}
