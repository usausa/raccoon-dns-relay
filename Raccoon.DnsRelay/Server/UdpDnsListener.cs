namespace Raccoon.DnsRelay.Server;

using Microsoft.Extensions.Options;

using Raccoon.DnsRelay.Protocol;
using Raccoon.DnsRelay.Settings;
using Raccoon.DnsRelay.Telemetry;

// Receives DNS queries over UDP using a raw socket and pooled buffers,
// dispatching each datagram to the handler under a concurrency limit
internal sealed class UdpDnsListener : IDnsListener
{
    private readonly ServerSetting options;
    private readonly DnsRelayMetrics metrics;
    private readonly ILogger<UdpDnsListener> log;
    private readonly SemaphoreSlim throttle;

    private Socket? socket;

    public UdpDnsListener(IOptions<ServerSetting> options, DnsRelayMetrics metrics, ILogger<UdpDnsListener> log)
    {
        this.options = options.Value;
        this.metrics = metrics;
        this.log = log;
        throttle = new SemaphoreSlim(this.options.MaxConcurrentQueries, this.options.MaxConcurrentQueries);
    }

    public async Task RunAsync(DnsQueryHandler handler, CancellationToken cancellationToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(options.Address), options.Port);
        var listenSocket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        listenSocket.Bind(endpoint);
        socket = listenSocket;
        log.InfoListening(endpoint);

        var pool = ArrayPool<byte>.Shared;
        while (!cancellationToken.IsCancellationRequested)
        {
            // A plain pooled array (not RentedBuffer) is dispatched to the fire-and-forget
            // processor, which returns it to the pool when done.
            var array = pool.Rent(options.ReceiveBufferSize);
            var clientAddress = new SocketAddress(endpoint.AddressFamily);

            int received;
            try
            {
                received = await listenSocket.ReceiveFromAsync(array, SocketFlags.None, clientAddress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                pool.Return(array);
                break;
            }
            catch (SocketException ex)
            {
                pool.Return(array);
                log.WarnReceiveError(ex);
                continue;
            }

            if (received < DnsHeader.Length)
            {
                pool.Return(array);
                metrics.QueryDropped("invalid");
                continue;
            }

            if (!await throttle.WaitAsync(0, CancellationToken.None))
            {
                pool.Return(array);
                metrics.QueryDropped("overload");
                log.WarnQueryDropped();
                continue;
            }

            _ = ProcessAsync(handler, array, received, clientAddress, cancellationToken);
        }
    }

#pragma warning disable CA1031
    private async Task ProcessAsync(DnsQueryHandler handler, byte[] array, int received, SocketAddress clientAddress, CancellationToken cancellationToken)
    {
        try
        {
            var request = new DnsRequest(array.AsMemory(0, received));
            var result = await handler(request, cancellationToken);
            try
            {
                if (result.Success && (socket is not null))
                {
                    await socket.SendToAsync(result.Memory, SocketFlags.None, clientAddress, cancellationToken);
                }
            }
            finally
            {
                result.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown in progress.
        }
        catch (SocketException ex)
        {
            log.WarnSendError(ex);
        }
        catch (Exception ex)
        {
            log.ErrorQueryProcessing(ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(array);
            throttle.Release();
        }
    }
#pragma warning restore CA1031

    public ValueTask DisposeAsync()
    {
        socket?.Dispose();
        throttle.Dispose();
        return ValueTask.CompletedTask;
    }
}
