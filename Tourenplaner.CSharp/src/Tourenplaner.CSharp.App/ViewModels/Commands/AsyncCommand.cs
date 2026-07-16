using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.App.ViewModels.Commands;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onException;
    private bool _isExecuting;

    public AsyncCommand(
        Func<Task> executeAsync,
        Func<bool>? canExecute = null,
        Action<Exception>? onException = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
        _onException = onException;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            await _executeAsync();
        }
        catch (Exception ex)
        {
            if (_onException is not null)
            {
                _onException(ex);
                return;
            }

            AppMessageBox.Show(
                $"Der Vorgang konnte nicht abgeschlossen werden.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
