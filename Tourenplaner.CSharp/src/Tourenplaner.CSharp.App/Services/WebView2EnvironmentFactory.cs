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

        return CoreWebView2Environment.CreateAsync(userDataFolder: profileDirectory);
    }
}
