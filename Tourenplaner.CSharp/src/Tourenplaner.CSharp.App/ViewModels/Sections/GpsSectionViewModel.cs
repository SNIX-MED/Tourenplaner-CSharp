using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class GpsSectionViewModel : SectionViewModelBase
{
    private const string KtracUrl = "https://map.ktrac.ch/";
    private string _loadedUrl = KtracUrl;
    private string _statusText = "KTRAC wird geladen.";
    private bool _webView2Available;

    public GpsSectionViewModel() : base("GPS", "Minimal eingebettete KTRAC-Ansicht.")
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
        LoadedUrl = KtracUrl;
        StatusText = _webView2Available
            ? "KTRAC geladen."
            : "WebView2 nicht verfügbar. Bitte KTRAC im Browser öffnen.";
    }

    private void OpenInBrowser()
    {
        if (!Uri.TryCreate((LoadedUrl ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
        {
            StatusText = "Cannot open browser: URL is invalid.";
            return;
        }

        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        StatusText = "KTRAC im Browser geöffnet.";
    }
}
