namespace Tourenplaner.CSharp.App.Services;

internal sealed class AsyncDebounceCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeSpan _delay;
    private readonly Func<CancellationToken, Task> _callback;
    private CancellationTokenSource? _pendingCancellation;
    private bool _disposed;

    public AsyncDebounceCoordinator(
        TimeSpan delay,
        Func<CancellationToken, Task> callback)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(callback);
        _delay = delay;
        _callback = callback;
    }

    public void Schedule()
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            CancelPendingCore();
            cancellation = new CancellationTokenSource();
            _pendingCancellation = cancellation;
        }

        ExecuteAsync(cancellation).Forget();
    }

    public void CancelPending()
    {
        lock (_gate)
        {
            CancelPendingCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelPendingCore();
        }
    }

    private async Task ExecuteAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(_delay, cancellation.Token);
            await _callback(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pendingCancellation, cancellation))
                {
                    _pendingCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void CancelPendingCore()
    {
        var pending = _pendingCancellation;
        _pendingCancellation = null;
        pending?.Cancel();
    }
}
