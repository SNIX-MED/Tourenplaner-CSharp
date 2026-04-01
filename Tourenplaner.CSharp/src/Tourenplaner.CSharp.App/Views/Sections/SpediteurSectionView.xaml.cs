using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class SpediteurSectionView : UserControl
{
    private bool _webViewReady;
    private INotifyPropertyChanged? _currentNotifier;

    public SpediteurSectionView()
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
        if (e.PropertyName == nameof(SpediteurSectionViewModel.LoadedUrl))
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
            await SpediteurWebView.EnsureCoreWebView2Async();
            SpediteurWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            SpediteurWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webViewReady = true;
            SpediteurWebView.Visibility = Visibility.Visible;
            FallbackNotice.Visibility = Visibility.Collapsed;
            if (DataContext is SpediteurSectionViewModel vm)
            {
                vm.SetWebView2RuntimeStatus(true);
            }

            NavigateIfPossible();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            SpediteurWebView.Visibility = Visibility.Collapsed;
            FallbackNotice.Visibility = Visibility.Visible;
            if (DataContext is SpediteurSectionViewModel vm)
            {
                vm.SetWebView2RuntimeStatus(false, "WebView2 runtime missing. Please use browser fallback.");
            }
        }
        catch (Exception ex)
        {
            SpediteurWebView.Visibility = Visibility.Collapsed;
            FallbackNotice.Visibility = Visibility.Visible;
            if (DataContext is SpediteurSectionViewModel vm)
            {
                vm.SetWebView2RuntimeStatus(false, $"Embedded Spediteur portal failed: {ex.Message}");
            }
        }
    }

    private void NavigateIfPossible()
    {
        if (!_webViewReady || DataContext is not SpediteurSectionViewModel vm)
        {
            return;
        }

        if (!Uri.TryCreate(vm.LoadedUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        SpediteurWebView.Source = uri;
    }
}
