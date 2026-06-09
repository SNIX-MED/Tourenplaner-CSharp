using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tourenplaner.CSharp.Launcher;

internal sealed class UpdateService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<UpdateConfiguration?> LoadConfigurationAsync(string baseDirectory, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(baseDirectory, "update-config.json");
        if (!File.Exists(configPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(configPath);
        var config = await ReadJsonAsync<UpdateConfiguration>(stream, cancellationToken);
        if (config is null || string.IsNullOrWhiteSpace(config.ManifestUrl))
        {
            return null;
        }

        config.ManifestUrl = config.ManifestUrl.Trim();
        return config;
    }

    public static Version GetCurrentVersion()
    {
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        return assemblyVersion is null || assemblyVersion == new Version(0, 0, 0, 0)
            ? new Version(1, 0, 0, 0)
            : assemblyVersion;
    }

    public static async Task<UpdateManifest?> GetAvailableUpdateAsync(
        UpdateConfiguration configuration,
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(configuration.ManifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await ReadJsonAsync<UpdateManifest>(stream, cancellationToken);
        if (manifest is null ||
            string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.InstallerUrl))
        {
            return null;
        }

        if (!Version.TryParse(manifest.Version, out var availableVersion))
        {
            return null;
        }

        return availableVersion > currentVersion ? manifest with { ParsedVersion = availableVersion } : null;
    }

    public static async Task<string> DownloadInstallerAsync(
        UpdateManifest manifest,
        string targetDirectory,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);

        var fileName = Path.GetFileName(new Uri(manifest.InstallerUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "GAWELA-Tourenplaner-Setup.exe";
        }

        var uniqueDirectory = Path.Combine(targetDirectory, DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(uniqueDirectory);
        var destinationPath = Path.Combine(uniqueDirectory, fileName);

        using (var response = await Client.GetAsync(manifest.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);

            var buffer = new byte[81920];
            long downloadedBytes = 0;
            while (true)
            {
                var read = await responseStream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;
                progress?.Report(new UpdateDownloadProgress(downloadedBytes, totalBytes));
            }

            await fileStream.FlushAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            var actualHash = ComputeSha256(destinationPath);
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Die heruntergeladene Update-Datei ist beschaedigt oder unvollstaendig.");
            }
        }

        return destinationPath;
    }

    public static string CreateInstallerScript(string installerPath, string launcherPath, int launcherProcessId)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), "GAWELA-Tourenplaner", $"apply-update-{Guid.NewGuid():N}.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);

        var script = $$"""
        $ErrorActionPreference = 'Stop'
        while (Get-Process -Id {{launcherProcessId}} -ErrorAction SilentlyContinue) {
            Start-Sleep -Milliseconds 400
        }

        $installer = '{{EscapePowerShell(installerPath)}}'
        $launcher = '{{EscapePowerShell(launcherPath)}}'
        $arguments = @('/CLOSEAPPLICATIONS', '/FORCECLOSEAPPLICATIONS')

        for ($i = 0; $i -lt 40; $i++) {
            try {
                $stream = [System.IO.File]::Open($installer, 'Open', 'Read', 'ReadWrite')
                $stream.Dispose()
                break
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }

        $process = Start-Process -FilePath $installer -ArgumentList $arguments -Verb RunAs -Wait -PassThru
        if ($process.ExitCode -eq 0 -and (Test-Path $launcher)) {
            Start-Process -FilePath $launcher
        }
        """;

        File.WriteAllText(scriptPath, script, Encoding.ASCII);
        return scriptPath;
    }

    private static string EscapePowerShell(string path)
    {
        return path.Replace("'", "''", StringComparison.Ordinal);
    }

    private static async Task<T?> ReadJsonAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var normalized = content.Trim().TrimStart('\uFEFF');
        return string.IsNullOrWhiteSpace(normalized)
            ? default
            : JsonSerializer.Deserialize<T>(normalized, JsonOptions);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GAWELA-Tourenplaner-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}

internal sealed class UpdateConfiguration
{
    public string ManifestUrl { get; set; } = string.Empty;
}

internal sealed record UpdateManifest
{
    public string Version { get; init; } = string.Empty;

    public string InstallerUrl { get; init; } = string.Empty;

    public string? Sha256 { get; init; }

    public string? ReleaseNotes { get; init; }

    public Version? ParsedVersion { get; init; }
}

internal readonly record struct UpdateDownloadProgress(long DownloadedBytes, long? TotalBytes)
{
    public double? Percent => TotalBytes is > 0 ? DownloadedBytes * 100d / TotalBytes.Value : null;
}
