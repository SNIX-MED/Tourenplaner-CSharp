using System.Diagnostics;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class GpsSectionViewModel : SectionViewModelBase
{
    private string _configuredUrl = AppSettings.DefaultGpsToolUrl;
    private string _loadedUrl = AppSettings.DefaultGpsToolUrl;
    private string _statusText = "GPS wird geladen.";
    private bool _webView2Available;

    public GpsSectionViewModel() : base("GPS", "Minimal eingebettete GPS-Ansicht.")
    {
        RefreshCommand = new DelegateCommand(Refresh);
        OpenInBrowserCommand = new DelegateCommand(OpenInBrowser);

        DetectWebView2();
        Refresh();
    }

    public ICommand RefreshCommand { get; }

    public ICommand OpenInBrowserCommand { get; }

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

    public void SetConfiguredUrl(string? url)
    {
        var normalized = (url ?? string.Empty).Trim();
        _configuredUrl = Uri.TryCreate(normalized, UriKind.Absolute, out _)
            ? normalized
            : AppSettings.DefaultGpsToolUrl;
        Refresh();
    }

    public void SetWebView2RuntimeStatus(bool available, string? details = null)
    {
        _webView2Available = available;
        if (!string.IsNullOrWhiteSpace(details))
        {
            StatusText = details;
        }
    }

    private void DetectWebView2()
    {
        // Runtime detection without hard package dependency, used for fallback information.
        _webView2Available = Type.GetType("Microsoft.Web.WebView2.Wpf.WebView2, Microsoft.Web.WebView2.Wpf", throwOnError: false) is not null;
    }

    private void Refresh()
    {
        LoadedUrl = _configuredUrl;
        StatusText = _webView2Available
            ? "GPS geladen."
            : "WebView2 nicht verfuegbar. Bitte GPS im Browser oeffnen.";
    }

    private void OpenInBrowser()
    {
        if (!Uri.TryCreate((LoadedUrl ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
        {
            StatusText = "Browser kann nicht geoeffnet werden: URL ist ungueltig.";
            return;
        }

        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        StatusText = "GPS im Browser geoeffnet.";
    }
}
