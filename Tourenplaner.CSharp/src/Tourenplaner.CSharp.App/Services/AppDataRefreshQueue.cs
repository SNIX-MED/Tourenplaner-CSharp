namespace Tourenplaner.CSharp.App.Services;

/// <summary>Combine a burst of notifications and keep at most one follow-up batch during a reload.</summary>
internal sealed class AppDataRefreshQueue(Func<IReadOnlyList<AppDataChangedEventArgs>, Task> refresh,
    TimeSpan? debounce = null)
{
    private readonly object _gate = new();
    private readonly List<AppDataChangedEventArgs> _pending = [];
    private TaskCompletionSource? _completion;

    public Task EnqueueAsync(AppDataChangedEventArgs change)
    {
        lock (_gate)
        {
            if (!_pending.Contains(change)) _pending.Add(change);
            if (_completion is not null) return _completion.Task;
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var completion = _completion;
            DrainAsync(completion).Forget();
            return completion.Task;
        }
    }

    private async Task DrainAsync(TaskCompletionSource completion)
    {
        AppDataChangedEventArgs[] batch = [];
        try
        {
            await Task.Yield();
            while (true)
            {
                // Always yield before reading the batch, including in deterministic tests.
                await Task.Delay(debounce ?? TimeSpan.FromMilliseconds(150));
                lock (_gate) { batch = _pending.ToArray(); _pending.Clear(); }
                await refresh(batch);
                lock (_gate)
                {
                    if (_pending.Count != 0) continue;
                    _completion = null;
                    completion.TrySetResult();
                    return;
                }
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                // Keep both the failed batch and notifications received during the failed read.
                // The next notification retries them; an unavailable server must not cause a busy loop.
                var retry = batch.Concat(_pending).Distinct().ToArray();
                _pending.Clear();
                _pending.AddRange(retry);
                _completion = null;
                completion.TrySetException(error);
            }
        }
    }
}
