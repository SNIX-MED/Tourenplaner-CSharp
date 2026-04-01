using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class GpsSectionView : UserControl
{
    private bool _webViewReady;
    private INotifyPropertyChanged? _currentNotifier;
    private string? _lastNavigatedUrl;

    public GpsSectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureWebViewInitializedAsync();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_currentNotifier is not null)
        {
            _currentNotifier.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _currentNotifier = DataContext as INotifyPropertyChanged;
        if (_currentNotifier is not null)
        {
            _currentNotifier.PropertyChanged += OnViewModelPropertyChanged;
        }

        NavigateIfPossible();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GpsSectionViewModel.LoadedUrl))
        {
            NavigateIfPossible();
        }
    }

    private async Task EnsureWebViewInitializedAsync()
    {
        if (_webViewReady)
        {
            return;
        }

        try
        {
            await GpsWebView.EnsureCoreWebView2Async();
            GpsWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            GpsWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webViewReady = true;
            GpsWebView.Visibility = Visibility.Visible;
            FallbackNotice.Visibility = Visibility.Collapsed;
            if (DataContext is GpsSectionViewModel vm)
            {
                vm.SetWebView2RuntimeStatus(true);
            }

            NavigateIfPossible();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            GpsWebView.Visibility = Visibility.Collapsed;
            FallbackNotice.Visibility = Visibility.Visible;
            if (DataContext is GpsSectionViewModel vm)
            {
                vm.SetWebView2RuntimeStatus(false, "WebView2 runtime missing. Please use browser fallback.");
            }
        }
        catch (Exception ex)
        {
            GpsWebView.Visibility = Visibility.Collapsed;
            FallbackNotice.Visibility = Visibility.Visible;
            if (DataContext is GpsSectionViewModel vm)
            {
                vm.SetWebView2RuntimeStatus(false, $"Embedded GPS failed: {ex.Message}");
            }
        }
    }

    private void NavigateIfPossible()
    {
        if (!_webViewReady || DataContext is not GpsSectionViewModel vm)
        {
            return;
        }

        if (!Uri.TryCreate(vm.LoadedUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        var targetUrl = uri.AbsoluteUri;
        if (string.Equals(_lastNavigatedUrl, targetUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        GpsWebView.Source = uri;
        _lastNavigatedUrl = targetUrl;
    }
}
