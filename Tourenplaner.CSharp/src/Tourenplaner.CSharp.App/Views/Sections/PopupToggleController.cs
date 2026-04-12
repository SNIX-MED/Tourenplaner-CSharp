using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Tourenplaner.CSharp.App.Views.Sections;

internal sealed class PopupToggleController : IDisposable
{
    private readonly Button _button;
    private readonly Popup _popup;
    private readonly Action? _onOpening;
    private bool _suppressNextClick;

    public PopupToggleController(Button button, Popup popup, Action? onOpening = null)
    {
        _button = button;
        _popup = popup;
        _onOpening = onOpening;

        _button.PreviewMouseLeftButtonDown += OnButtonPreviewMouseLeftButtonDown;
        _button.Click += OnButtonClick;
    }

    public void Close()
    {
        _popup.IsOpen = false;
    }

    public void Dispose()
    {
        _button.PreviewMouseLeftButtonDown -= OnButtonPreviewMouseLeftButtonDown;
        _button.Click -= OnButtonClick;
    }

    private void OnButtonPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_popup.IsOpen)
        {
            return;
        }

        _popup.IsOpen = false;
        _suppressNextClick = true;
        e.Handled = true;
    }

    private void OnButtonClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            e.Handled = true;
            return;
        }

        if (_popup.IsOpen)
        {
            _popup.IsOpen = false;
            e.Handled = true;
            return;
        }

        _onOpening?.Invoke();
        _popup.IsOpen = true;
        e.Handled = true;
    }
}
