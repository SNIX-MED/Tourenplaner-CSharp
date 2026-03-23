using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Tourenplaner.CSharp.App.Themes;

public static class AppThemeManager
{
    private const string DarkThemePath = "Themes/Theme.Dark.xaml";
    private const string LightThemePath = "Themes/Theme.Light.xaml";
    private static string _preferencesPath = string.Empty;

    public static bool IsDarkTheme { get; private set; } = true;

    public static void Initialize(string dataRootPath)
    {
        _preferencesPath = Path.Combine(dataRootPath, "theme.preferences.json");
        var stored = LoadStoredPreference();
        var dark = stored ?? DetectSystemDarkMode() ?? true;
        ApplyTheme(dark, persist: false);
    }

    public static void ToggleTheme()
    {
        ApplyTheme(!IsDarkTheme, persist: true);
    }

    public static void ApplyTheme(bool darkTheme, bool persist = true)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            IsDarkTheme = darkTheme;
            return;
        }

        var merged = app.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(d =>
            d.Source is not null &&
            (d.Source.OriginalString.Contains("Themes/Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
             d.Source.OriginalString.Contains("Themes/Theme.Light.xaml", StringComparison.OrdinalIgnoreCase)));

        var source = new Uri(darkTheme ? DarkThemePath : LightThemePath, UriKind.Relative);
        if (existing is null)
        {
            merged.Add(new System.Windows.ResourceDictionary { Source = source });
        }
        else
        {
            existing.Source = source;
        }

        IsDarkTheme = darkTheme;
        if (persist)
        {
            SavePreference(darkTheme);
        }
    }

    private static bool? LoadStoredPreference()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_preferencesPath) || !File.Exists(_preferencesPath))
            {
                return null;
            }

            var json = File.ReadAllText(_preferencesPath);
            var model = JsonSerializer.Deserialize<ThemePreference>(json);
            return model?.Theme?.Equals("dark", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return null;
        }
    }

    private static void SavePreference(bool darkTheme)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_preferencesPath))
            {
                return;
            }

            var model = new ThemePreference { Theme = darkTheme ? "dark" : "light" };
            var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_preferencesPath, json);
        }
        catch
        {
            // Non-critical, ignore persistence errors.
        }
    }

    private static bool? DetectSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int lightFlag)
            {
                return lightFlag == 0;
            }
        }
        catch
        {
            // Ignore and fall back.
        }

        return null;
    }

    private sealed class ThemePreference
    {
        public string Theme { get; set; } = "dark";
    }
}
