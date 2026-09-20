using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Wisp.App.Services.Hosting;

public sealed class LocalTraceService(ILogger<LocalTraceService> logger) : IHostedService, IDisposable
{
    private readonly TraceListener listener = new LocalTraceListener(logger);
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Trace.Listeners.Clear();
        Trace.Listeners.Add(listener);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Trace.Listeners.Remove(listener);
        return Task.CompletedTask;
    }

    public void Dispose() => listener.Dispose();

    private sealed class LocalTraceListener(ILogger logger) : TraceListener
    {
        public override bool IsThreadSafe => true;
        public override void Write(string? message) => logger.LogInformation("{TraceMessage}", message);
        public override void WriteLine(string? message) => Write(message);
        public override void TraceEvent(TraceEventCache? eventCache, string source,
            TraceEventType eventType, int id, string? message) =>
            logger.Log(eventType is TraceEventType.Error or TraceEventType.Critical ? LogLevel.Error
                : eventType == TraceEventType.Warning ? LogLevel.Warning : LogLevel.Information,
                "{TraceMessage}", message);
    }
}
