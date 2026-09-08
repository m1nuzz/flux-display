using Microsoft.UI.Dispatching;

namespace FluxDisplay.App.Helpers;

public static class AsyncHelper
{
    public static void FireAndForget(Func<Task> work, Action<Exception>? onError = null)
    {
        _ = RunAsync();

        async Task RunAsync()
        {
            try
            {
                await work().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }
    }

    public static bool TryEnqueue(Action action)
    {
        var queue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
        return queue is not null && queue.TryEnqueue(() => action());
    }

    public static Task EnqueueAsync(Action action)
    {
        var queue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
        if (queue is null)
        {
            action();
            return Task.CompletedTask;
        }

        if (queue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        if (!queue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }))
        {
            action();
            return Task.CompletedTask;
        }

        return tcs.Task;
    }
}
