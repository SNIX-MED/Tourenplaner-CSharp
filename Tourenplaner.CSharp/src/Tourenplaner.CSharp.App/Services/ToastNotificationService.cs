namespace Tourenplaner.CSharp.App.Services;

public sealed record ToastNotification(string Message, int DurationMs = 4000);

public static class ToastNotificationService
{
    public static event EventHandler<ToastNotification>? NotificationRequested;

    public static void ShowInfo(string message, int durationMs = 4000)
    {
        var text = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var normalizedDuration = Math.Clamp(durationMs, 1500, 10000);
        NotificationRequested?.Invoke(null, new ToastNotification(text, normalizedDuration));
    }
}
