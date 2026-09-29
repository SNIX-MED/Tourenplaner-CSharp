using System.IO;
using System.Collections.Concurrent;
using Microsoft.Web.WebView2.Core;

namespace Tourenplaner.CSharp.App.Services;

internal static class WebView2EnvironmentFactory
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<CoreWebView2Environment>>> Environments =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task<CoreWebView2Environment> CreateAsync(string profileName)
    {
        var normalizedProfile = string.IsNullOrWhiteSpace(profileName)
            ? "Default"
            : string.Concat(profileName.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

        var lazyEnvironment = Environments.GetOrAdd(
            normalizedProfile,
            static profile => new Lazy<Task<CoreWebView2Environment>>(
                () => CreateEnvironmentCoreAsync(profile),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyEnvironment.Value;
    }

    private static Task<CoreWebView2Environment> CreateEnvironmentCoreAsync(string profileName)
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GAWELA",
            "Tourenplaner",
            "WebView2");

        Directory.CreateDirectory(baseDirectory);

        var profileDirectory = Path.Combine(baseDirectory, profileName);
        Directory.CreateDirectory(profileDirectory);

        CoreWebView2EnvironmentOptions? options = null;
        if (string.Equals(profileName, "Map", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profileName, "PinPreview", StringComparison.OrdinalIgnoreCase))
        {
            // Keep map colors consistent on monitors with problematic color profiles.
            options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--force-color-profile=srgb"
            };
        }

        return CoreWebView2Environment.CreateAsync(userDataFolder: profileDirectory, options: options);
    }

    public static void ConfigurePersistentLoginStorage(CoreWebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);

        webView.Settings.IsGeneralAutofillEnabled = true;
        webView.Settings.IsPasswordAutosaveEnabled = true;

        webView.Profile.IsGeneralAutofillEnabled = true;
        webView.Profile.IsPasswordAutosaveEnabled = true;
    }
}
