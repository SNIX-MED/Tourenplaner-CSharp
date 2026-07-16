using System.Diagnostics;

namespace Tourenplaner.CSharp.App.Services;

internal static class TaskExtensions
{
    public static void Forget(this Task task, Action<Exception>? onError = null)
    {
        if (task.IsCompleted)
        {
            ObserveCompletion(task, onError);
            return;
        }

        _ = ObserveCompletionAsync(task, onError);
    }

    private static async Task ObserveCompletionAsync(Task task, Action<Exception>? onError)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected for debounced and shutdown-triggered background operations.
        }
        catch (Exception ex)
        {
            HandleError(ex, onError);
        }
    }

    private static void ObserveCompletion(Task task, Action<Exception>? onError)
    {
        if (task.IsCanceled || task.Exception is null)
        {
            return;
        }

        HandleError(task.Exception.GetBaseException(), onError);
    }

    private static void HandleError(Exception ex, Action<Exception>? onError)
    {
        if (onError is not null)
        {
            onError(ex);
            return;
        }

        Debug.WriteLine($"Background task failed: {ex}");
    }
}
