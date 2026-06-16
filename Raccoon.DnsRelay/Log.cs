namespace Raccoon.DnsRelay;

using System.Runtime;

internal static partial class Log
{
    // Startup

    [LoggerMessage(Level = LogLevel.Information, Message = "Service start.")]
    public static partial void InfoServiceStart(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Environment: version=[{version}], runtime=[{runtime}], directory=[{directory}]")]
    public static partial void InfoServiceEnvironment(this ILogger logger, Version? version, Version runtime, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "GCSettings: serverGC=[{isServerGC}], latencyMode=[{latencyMode}], largeObjectHeapCompactionMode=[{largeObjectHeapCompactionMode}]")]
    public static partial void InfoServiceGC(this ILogger logger, bool isServerGC, GCLatencyMode latencyMode, GCLargeObjectHeapCompactionMode largeObjectHeapCompactionMode);

    [LoggerMessage(Level = LogLevel.Information, Message = "ThreadPool: workerThreads=[{workerThreads}], completionPortThreads=[{completionPortThreads}]")]
    public static partial void InfoServiceThreadPool(this ILogger logger, int workerThreads, int completionPortThreads);

    // Listener

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening on {endpoint} (UDP).")]
    public static partial void InfoListening(this ILogger logger, EndPoint endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Receive error.")]
    public static partial void WarnReceiveError(this ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Send error.")]
    public static partial void WarnSendError(this ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Query dropped due to concurrency limit.")]
    public static partial void WarnQueryDropped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error while processing query.")]
    public static partial void ErrorQueryProcessing(this ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalid query received.")]
    public static partial void DebugInvalidQuery(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Query. id=[{id}], name=[{name}], type=[{type}]")]
    public static partial void DebugQueryReceived(this ILogger logger, ushort id, string name, ushort type);

    // Query

    [LoggerMessage(Level = LogLevel.Information, Message = "Query. client=[{client}], name=[{name}], type=[{type}], result=[{result}]")]
    public static partial void InfoQueryResolved(this ILogger logger, string client, string name, string type, string result);

    // Upstream

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream {endpoint} timed out.")]
    public static partial void WarnUpstreamTimeout(this ILogger logger, EndPoint endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream {endpoint} socket error.")]
    public static partial void WarnUpstreamSocketError(this ILogger logger, EndPoint endpoint, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resolved by upstream {endpoint} ({bytes} bytes).")]
    public static partial void DebugUpstreamResolved(this ILogger logger, EndPoint endpoint, int bytes);

    // Cache

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit. id=[{id}].")]
    public static partial void DebugCacheHit(this ILogger logger, ushort id);
}
