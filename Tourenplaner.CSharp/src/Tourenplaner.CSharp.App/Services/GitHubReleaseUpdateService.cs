using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace Tourenplaner.CSharp.App.Services;

public sealed class GitHubReleaseUpdateService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly string[] PreferredAssetExtensions = [".msi", ".exe", ".msixbundle", ".msix", ".appinstaller", ".zip"];

    public async Task<GitHubReleaseInfo> GetLatestReleaseAsync(string updateFeedUrl, CancellationToken cancellationToken = default)
    {
        var repository = ParseRepository(updateFeedUrl);
        var apiUrl = $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/releases/latest";

        using var response = await Client.GetAsync(apiUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub release lookup failed with status {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(
            stream,
            cancellationToken: cancellationToken);

        if (payload is null || string.IsNullOrWhiteSpace(payload.TagName) || string.IsNullOrWhiteSpace(payload.HtmlUrl))
        {
            throw new InvalidOperationException("GitHub release payload was incomplete.");
        }

        return new GitHubReleaseInfo(
            repository.Owner,
            repository.Name,
            payload.TagName.Trim(),
            string.IsNullOrWhiteSpace(payload.Name) ? payload.TagName.Trim() : payload.Name.Trim(),
            payload.HtmlUrl.Trim(),
            payload.PublishedAt,
            payload.Prerelease,
            payload.Assets?
                .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                .Select(asset => new GitHubReleaseAssetInfo(
                    asset.Name!.Trim(),
                    asset.BrowserDownloadUrl!.Trim(),
                    asset.Size))
                .ToList() ?? []);
    }

    public GitHubReleaseAssetInfo? SelectPreferredAsset(GitHubReleaseInfo release)
    {
        foreach (var extension in PreferredAssetExtensions)
        {
            var asset = release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
            if (asset is not null)
            {
                return asset;
            }
        }

        return release.Assets.FirstOrDefault();
    }

    public async Task<string> DownloadAssetAsync(GitHubReleaseAssetInfo asset, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);

        var targetPath = GetUniqueTargetPath(destinationDirectory, asset.Name);
        using var response = await Client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub asset download failed with status {(int)response.StatusCode}.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(targetPath);
        await input.CopyToAsync(output, cancellationToken);
        return targetPath;
    }

    public static Version? TryParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        normalized = normalized.TrimStart('v', 'V');

        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GAWELA-Tourenplaner", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static GitHubRepository ParseRepository(string updateFeedUrl)
    {
        if (!Uri.TryCreate((updateFeedUrl ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Update URL is invalid.");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length >= 3 && string.Equals(segments[0], "repos", StringComparison.OrdinalIgnoreCase))
            {
                return new GitHubRepository(segments[1], segments[2]);
            }
        }
        else if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length >= 2)
            {
                return new GitHubRepository(segments[0], segments[1]);
            }
        }

        throw new InvalidOperationException("Update URL must point to a GitHub repository.");
    }

    private static string GetUniqueTargetPath(string destinationDirectory, string fileName)
    {
        var safeName = Path.GetFileName(fileName) ?? "update-package";
        var candidatePath = Path.Combine(destinationDirectory, safeName);
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var baseName = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        var suffix = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(destinationDirectory, $"{baseName}-{suffix}{extension}");
    }

    private sealed record GitHubRepository(string Owner, string Name);

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAssetResponse>? Assets { get; set; }
    }

    private sealed class GitHubReleaseAssetResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}

public sealed record GitHubReleaseInfo(
    string Owner,
    string Repository,
    string TagName,
    string ReleaseName,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    IReadOnlyList<GitHubReleaseAssetInfo> Assets);

public sealed record GitHubReleaseAssetInfo(
    string Name,
    string DownloadUrl,
    long SizeBytes);
