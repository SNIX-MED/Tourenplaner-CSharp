using System.Windows;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.App.Services;

public static class AppMessageBox
{
    public static MessageBoxResult Show(string messageBoxText)
        => Show(System.Windows.Application.Current?.MainWindow, messageBoxText, "Hinweis", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption)
        => Show(System.Windows.Application.Current?.MainWindow, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
        => Show(System.Windows.Application.Current?.MainWindow, messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        => Show(System.Windows.Application.Current?.MainWindow, messageBoxText, caption, button, icon);

    public static MessageBoxResult Show(Window? owner, string messageBoxText)
        => Show(owner, messageBoxText, "Hinweis", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption)
        => Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button)
        => Show(owner, messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        if (System.Windows.Application.Current is null)
        {
            return MessageBoxResult.None;
        }

        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            return System.Windows.Application.Current.Dispatcher.Invoke(
                () => Show(owner, messageBoxText, caption, button, icon));
        }

        var dialog = new AppMessageDialogWindow(messageBoxText, caption, button, icon)
        {
            Owner = owner ?? System.Windows.Application.Current.MainWindow
        };

        _ = dialog.ShowDialog();
        return dialog.Result;
    }
}
