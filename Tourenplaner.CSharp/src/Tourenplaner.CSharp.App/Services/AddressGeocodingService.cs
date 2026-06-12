using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Concurrent;
using System.IO;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class AddressGeocodingService
{
    private const double SwitzerlandCenterLat = 46.798562;
    private const double SwitzerlandCenterLon = 8.231974;
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim CacheGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, GeoPoint> InMemoryCache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<GeoPoint?> TryGeocodeOrderAsync(Order order, string? tomTomApiKey = null, string? cacheFilePath = null)
    {
        var street = BuildStreetLine(order.DeliveryAddress?.Street, order.DeliveryAddress?.HouseNumber);
        var postalCode = (order.DeliveryAddress?.PostalCode ?? string.Empty).Trim();
        var city = (order.DeliveryAddress?.City ?? string.Empty).Trim();
        var fallback = (order.Address ?? string.Empty).Trim();
        return await TryGeocodeAddressAsync(street, postalCode, city, fallback, tomTomApiKey, cacheFilePath);
    }

    public static async Task<GeoPoint?> TryGeocodeAddressAsync(
        string? street,
        string? postalCode,
        string? city,
        string? fallbackAddress = null,
        string? tomTomApiKey = null,
        string? cacheFilePath = null)
    {
        var queries = BuildQueries(
            (street ?? string.Empty).Trim(),
            (postalCode ?? string.Empty).Trim(),
            (city ?? string.Empty).Trim(),
            (fallbackAddress ?? string.Empty).Trim());

        foreach (var query in queries)
        {
            var key = NormalizeWhitespace(query).ToLowerInvariant();
            if (TryGetCachedLocation(key, out var cached) && cached is not null)
            {
                return cached;
            }

            var persistedCache = await TryLoadCacheFromFileAsync(cacheFilePath);
            if (persistedCache.TryGetValue(key, out var persisted))
            {
                InMemoryCache[key] = persisted;
                return persisted;
            }

            var result = await TryGeocodeQueryAsync(query, tomTomApiKey);
            if (result is not null)
            {
                InMemoryCache[key] = result;
                await TrySaveCacheEntryAsync(cacheFilePath, key, result);
                return result;
            }
        }

        return null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GAWELA-Tourenplaner/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private static IReadOnlyList<string> BuildQueries(string street, string postalCode, string city, string fallback)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(street) && (!string.IsNullOrWhiteSpace(postalCode) || !string.IsNullOrWhiteSpace(city)))
        {
            candidates.Add($"{street}, {postalCode} {city}, Schweiz");
        }

        if (!string.IsNullOrWhiteSpace(street) && !string.IsNullOrWhiteSpace(city))
        {
            candidates.Add($"{street}, {city}, Schweiz");
        }

        if (!string.IsNullOrWhiteSpace(postalCode) && !string.IsNullOrWhiteSpace(city))
        {
            candidates.Add($"{postalCode} {city}, Schweiz");
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            candidates.Add($"{city}, Schweiz");
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            candidates.Add(EnsureCountry(fallback));
            candidates.Add(NormalizeWhitespace(fallback));
        }

        var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var candidate in candidates)
        {
            var value = NormalizeWhitespace(candidate);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (string.Equals(value, "Schweiz", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Switzerland", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (dedup.Add(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static string BuildStreetLine(string? street, string? houseNumber)
    {
        return string.Join(" ", new[]
        {
            (street ?? string.Empty).Trim(),
            (houseNumber ?? string.Empty).Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static async Task<GeoPoint?> TryGeocodeQueryAsync(string query, string? tomTomApiKey)
    {
        var key = (tomTomApiKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return await TryGeocodeWithTomTomAsync(query, key);
    }

    private static async Task<GeoPoint?> TryGeocodeWithTomTomAsync(string query, string apiKey)
    {
        var uri = $"https://api.tomtom.com/search/2/geocode/{Uri.EscapeDataString(query)}.json?key={Uri.EscapeDataString(apiKey)}&limit=1&countrySet=CH";
        try
        {
            using var response = await Client.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (!document.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return null;
            }

            var position = results[0].TryGetProperty("position", out var pos) ? pos : default;
            if (position.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!position.TryGetProperty("lat", out var latElement) || latElement.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            if (!position.TryGetProperty("lon", out var lonElement) || lonElement.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            return new GeoPoint(latElement.GetDouble(), lonElement.GetDouble());
        }
        catch
        {
            return null;
        }
    }

    private static string EnsureCountry(string value)
    {
        var normalized = NormalizeWhitespace(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Contains("schweiz", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("switzerland", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return $"{normalized}, Schweiz";
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', ',');
    }

    private static bool TryGetCachedLocation(string key, out GeoPoint? value)
    {
        return InMemoryCache.TryGetValue(key, out value);
    }

    private static async Task<Dictionary<string, GeoPoint>> TryLoadCacheFromFileAsync(string? cacheFilePath)
    {
        var path = (cacheFilePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
        }

        await CacheGate.WaitAsync();
        try
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
            }

            await using var stream = File.OpenRead(path);
            var payload = await JsonSerializer.DeserializeAsync<Dictionary<string, CacheEntry>>(stream)
                          ?? new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            return payload
                .Where(x => x.Value is not null)
                .ToDictionary(
                    x => x.Key,
                    x => new GeoPoint(x.Value!.Latitude, x.Value.Longitude),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            CacheGate.Release();
        }
    }

    private static async Task TrySaveCacheEntryAsync(string? cacheFilePath, string key, GeoPoint point)
    {
        var path = (cacheFilePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await CacheGate.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Dictionary<string, CacheEntry> payload;
            if (File.Exists(path))
            {
                await using var readStream = File.OpenRead(path);
                payload = await JsonSerializer.DeserializeAsync<Dictionary<string, CacheEntry>>(readStream)
                          ?? new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                payload = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            }

            payload[key] = new CacheEntry(point.Latitude, point.Longitude);

            await using var writeStream = File.Create(path);
            await JsonSerializer.SerializeAsync(writeStream, payload, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
        }
        finally
        {
            CacheGate.Release();
        }
    }

    public static bool IsLikelyCountryCentroid(GeoPoint? point)
    {
        if (point is null)
        {
            return false;
        }

        return Math.Abs(point.Latitude - SwitzerlandCenterLat) < 0.02 &&
               Math.Abs(point.Longitude - SwitzerlandCenterLon) < 0.02;
    }

    private sealed class CacheEntry
    {
        public CacheEntry()
        {
        }

        public CacheEntry(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
