using System.Threading.Channels;

namespace Wisp.Data;

internal static class SqliteWriteQueue
{
    private static readonly Channel<Func<Task>> Queue = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    // One consumer for the process, including independently constructed repositories/databases.
    static SqliteWriteQueue() => _ = Task.Run(ConsumeAsync);

    internal static Task<T> EnqueueAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<T>(ct);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Writer.TryWrite(async () =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                completion.SetResult(await operation().ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                completion.SetCanceled(ct);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    private static async Task ConsumeAsync()
    {
        await foreach (var operation in Queue.Reader.ReadAllAsync().ConfigureAwait(false))
            await operation().ConfigureAwait(false);
    }
}
