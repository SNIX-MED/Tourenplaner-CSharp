using System.Diagnostics;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class SpediteurSectionViewModel : SectionViewModelBase
{
    private string _configuredUrl = AppSettings.DefaultSpediteurToolUrl;
    private string _loadedUrl = AppSettings.DefaultSpediteurToolUrl;
    private string _statusText = "Spediteur-Portal wird geladen.";
    private bool _webView2Available;

    public SpediteurSectionViewModel() : base("Spediteur", "Eingebettetes Spediteur-Portal.")
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
            : AppSettings.DefaultSpediteurToolUrl;
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
        _webView2Available = Type.GetType("Microsoft.Web.WebView2.Wpf.WebView2, Microsoft.Web.WebView2.Wpf", throwOnError: false) is not null;
    }

    private void Refresh()
    {
        LoadedUrl = _configuredUrl;
        StatusText = _webView2Available
            ? "Spediteur-Portal geladen."
            : "WebView2 nicht verfuegbar. Bitte Spediteur-Portal im Browser oeffnen.";
    }

    private void OpenInBrowser()
    {
        if (!Uri.TryCreate((LoadedUrl ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
        {
            StatusText = "Browser kann nicht geoeffnet werden: URL ist ungueltig.";
            return;
        }

        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        StatusText = "Spediteur-Portal im Browser geoeffnet.";
    }
}
