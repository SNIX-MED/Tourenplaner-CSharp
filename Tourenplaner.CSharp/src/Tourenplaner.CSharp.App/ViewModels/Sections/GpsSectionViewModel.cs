using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class GpsSectionViewModel : SectionViewModelBase
{
    private string _gpsUrl = "https://www.openstreetmap.org";
    private string _loadedUrl = string.Empty;
    private string _statusText = "GPS view ready.";
    private bool _webView2Available;
    private DateTimeOffset _lastReloadAt;

    public GpsSectionViewModel() : base("GPS", "Vehicle GPS status, embedded map URL and fallback actions.")
    {
        RefreshCommand = new DelegateCommand(Refresh);
        OpenInBrowserCommand = new DelegateCommand(OpenInBrowser);
        CopyUrlCommand = new DelegateCommand(CopyUrl);

        DetectWebView2();
        Refresh();
    }

    public ICommand RefreshCommand { get; }

    public ICommand OpenInBrowserCommand { get; }

    public ICommand CopyUrlCommand { get; }

    public string GpsUrl
    {
        get => _gpsUrl;
        set => SetProperty(ref _gpsUrl, value);
    }

    public string LoadedUrl
    {
        get => _loadedUrl;
        private set => SetProperty(ref _loadedUrl, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool WebView2Available
    {
        get => _webView2Available;
        private set => SetProperty(ref _webView2Available, value);
    }

    public DateTimeOffset LastReloadAt
    {
        get => _lastReloadAt;
        private set => SetProperty(ref _lastReloadAt, value);
    }

    private void DetectWebView2()
    {
        // Runtime detection without hard package dependency, used for fallback information.
        WebView2Available = Type.GetType("Microsoft.Web.WebView2.Wpf.WebView2, Microsoft.Web.WebView2.Wpf", throwOnError: false) is not null;
    }

    private void Refresh()
    {
        if (!Uri.TryCreate((GpsUrl ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
        {
            StatusText = "Invalid GPS URL.";
            return;
        }

        LoadedUrl = uri.ToString();
        LastReloadAt = DateTimeOffset.Now;
        StatusText = WebView2Available
            ? $"Reloaded at {LastReloadAt:HH:mm:ss}. WebView2 runtime is available."
            : $"Reloaded at {LastReloadAt:HH:mm:ss}. WebView2 runtime not detected, using browser fallback.";
    }

    private void OpenInBrowser()
    {
        if (!Uri.TryCreate((LoadedUrl ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
        {
            StatusText = "Cannot open browser: URL is invalid.";
            return;
        }

        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        StatusText = "Opened GPS URL in external browser.";
    }

    private void CopyUrl()
    {
        if (string.IsNullOrWhiteSpace(LoadedUrl))
        {
            StatusText = "Nothing to copy.";
            return;
        }

        Clipboard.SetText(LoadedUrl);
        StatusText = "GPS URL copied to clipboard.";
    }
}
