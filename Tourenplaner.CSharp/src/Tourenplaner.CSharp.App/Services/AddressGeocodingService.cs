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
    private static readonly ConcurrentDictionary<string, AddressGeocodingResult> InMemoryResolutionCache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<GeoPoint?> TryGeocodeOrderAsync(Order order, string? tomTomApiKey = null, string? cacheFilePath = null)
    {
        return (await TryResolveOrderAsync(order, tomTomApiKey, cacheFilePath))?.Location;
    }

    public static async Task<AddressGeocodingResult?> TryResolveOrderAsync(Order order, string? tomTomApiKey = null, string? cacheFilePath = null)
    {
        var street = BuildStreetLine(order.DeliveryAddress?.Street, order.DeliveryAddress?.HouseNumber);
        var postalCode = (order.DeliveryAddress?.PostalCode ?? string.Empty).Trim();
        var city = (order.DeliveryAddress?.City ?? string.Empty).Trim();
        var fallback = (order.Address ?? string.Empty).Trim();
        return await TryResolveAddressAsync(street, postalCode, city, fallback, tomTomApiKey, cacheFilePath);
    }

    public static async Task<GeoPoint?> TryGeocodeAddressAsync(
        string? street,
        string? postalCode,
        string? city,
        string? fallbackAddress = null,
        string? tomTomApiKey = null,
        string? cacheFilePath = null)
    {
        return (await TryResolveAddressAsync(street, postalCode, city, fallbackAddress, tomTomApiKey, cacheFilePath))?.Location;
    }

    public static async Task<AddressGeocodingResult?> TryResolveAddressAsync(
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

        var persistedCache = await TryLoadCacheFromFileAsync(cacheFilePath);
        GeocodeCandidate? bestCandidate = null;
        var cacheKeys = new List<string>();

        foreach (var query in queries)
        {
            var key = NormalizeWhitespace(query).ToLowerInvariant();
            cacheKeys.Add(key);

            GeocodeCandidate? candidate = null;
            if (TryGetCachedResolution(key, out var cachedResolution) && cachedResolution is not null)
            {
                candidate = new GeocodeCandidate(cachedResolution.Location, cachedResolution.MatchType, cachedResolution.EntityType, query);
            }
            else if (TryGetCachedLocation(key, out var cached) && cached is not null)
            {
                candidate = new GeocodeCandidate(cached, "Cached", null, query);
            }
            else if (persistedCache.TryGetValue(key, out var persisted))
            {
                InMemoryCache[key] = persisted.Location;
                var persistedResult = new AddressGeocodingResult(
                    persisted.Location,
                    persisted.IsPrecise,
                    query,
                    persisted.MatchType,
                    persisted.EntityType);
                InMemoryResolutionCache[key] = persistedResult;
                candidate = new GeocodeCandidate(persisted.Location, persisted.MatchType, persisted.EntityType, query);
            }
            else
            {
                candidate = await TryGeocodeQueryAsync(query, tomTomApiKey);
                if (candidate is not null)
                {
                    InMemoryCache[key] = candidate.Point;
                    var resolution = CreateResolution(candidate);
                    InMemoryResolutionCache[key] = resolution;
                    persistedCache[key] = new CachedGeocodingResult(
                        candidate.Point,
                        resolution.MatchType,
                        resolution.EntityType,
                        resolution.IsPrecise);
                    await TrySaveCacheEntryAsync(cacheFilePath, key, persistedCache[key]);
                }
            }

            if (candidate is null)
            {
                continue;
            }

            if (IsBetterCandidate(candidate, bestCandidate))
            {
                bestCandidate = candidate;
            }

            if (IsExactAddressCandidate(candidate))
            {
                break;
            }
        }

        if (bestCandidate is null)
        {
            return null;
        }

        foreach (var key in cacheKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            InMemoryCache[key] = bestCandidate.Point;
            var bestResolution = CreateResolution(bestCandidate);
            InMemoryResolutionCache[key] = bestResolution;
            if (!persistedCache.TryGetValue(key, out var cachedResult) || !cachedResult.Matches(bestResolution))
            {
                var nextCachedResult = new CachedGeocodingResult(
                    bestResolution.Location,
                    bestResolution.MatchType,
                    bestResolution.EntityType,
                    bestResolution.IsPrecise);
                persistedCache[key] = nextCachedResult;
                await TrySaveCacheEntryAsync(cacheFilePath, key, nextCachedResult);
            }
        }

        return CreateResolution(bestCandidate);
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

    private static async Task<GeocodeCandidate?> TryGeocodeQueryAsync(string query, string? tomTomApiKey)
    {
        var key = (tomTomApiKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return await TryGeocodeWithTomTomAsync(query, key);
    }

    private static async Task<GeocodeCandidate?> TryGeocodeWithTomTomAsync(string query, string apiKey)
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

            var type = results[0].TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : string.Empty;
            var entityType = results[0].TryGetProperty("entityType", out var entityTypeElement) && entityTypeElement.ValueKind == JsonValueKind.String
                ? entityTypeElement.GetString()
                : null;

            return new GeocodeCandidate(
                new GeoPoint(latElement.GetDouble(), lonElement.GetDouble()),
                type ?? string.Empty,
                entityType,
                query);
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

    private static bool TryGetCachedResolution(string key, out AddressGeocodingResult? value)
    {
        return InMemoryResolutionCache.TryGetValue(key, out value);
    }

    private static AddressGeocodingResult CreateResolution(GeocodeCandidate candidate)
    {
        var normalizedType = NormalizeWhitespace(candidate.Type);
        var isPrecise =
            string.Equals(normalizedType, "Point Address", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedType, "Address Range", StringComparison.OrdinalIgnoreCase);

        return new AddressGeocodingResult(candidate.Point, isPrecise, candidate.Query, candidate.Type, candidate.EntityType);
    }

    private static bool IsBetterCandidate(GeocodeCandidate candidate, GeocodeCandidate? bestCandidate)
    {
        if (bestCandidate is null)
        {
            return true;
        }

        var candidateScore = GetCandidateScore(candidate);
        var bestScore = GetCandidateScore(bestCandidate);
        if (candidateScore != bestScore)
        {
            return candidateScore > bestScore;
        }

        return GetQuerySpecificityScore(candidate.Query) > GetQuerySpecificityScore(bestCandidate.Query);
    }

    private static bool IsExactAddressCandidate(GeocodeCandidate candidate)
    {
        return string.Equals(
            NormalizeWhitespace(candidate.Type),
            "Point Address",
            StringComparison.OrdinalIgnoreCase);
    }

    private static int GetCandidateScore(GeocodeCandidate candidate)
    {
        var normalizedType = NormalizeWhitespace(candidate.Type);
        var normalizedEntityType = NormalizeWhitespace(candidate.EntityType ?? string.Empty);
        var baseScore = normalizedType.ToLowerInvariant() switch
        {
            "point address" => 500,
            "address range" => 450,
            "street" => 350,
            "cross street" => 300,
            "geography" when string.Equals(normalizedEntityType, "MunicipalitySubdivision", StringComparison.OrdinalIgnoreCase) => 220,
            "geography" when string.Equals(normalizedEntityType, "PostalCodeArea", StringComparison.OrdinalIgnoreCase) => 120,
            "cached" => 100,
            "geography" => 150,
            _ => 180
        };

        return baseScore + GetQuerySpecificityScore(candidate.Query);
    }

    private static int GetQuerySpecificityScore(string query)
    {
        var normalized = NormalizeWhitespace(query);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 0;
        }

        var score = 0;
        if (normalized.Any(char.IsDigit))
        {
            score += 30;
        }

        if (normalized.Contains(',', StringComparison.Ordinal))
        {
            score += 10;
        }

        if (normalized.Contains("schweiz", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("switzerland", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        return score;
    }

    private static async Task<Dictionary<string, CachedGeocodingResult>> TryLoadCacheFromFileAsync(string? cacheFilePath)
    {
        var path = (cacheFilePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new Dictionary<string, CachedGeocodingResult>(StringComparer.OrdinalIgnoreCase);
        }

        await CacheGate.WaitAsync();
        try
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, CachedGeocodingResult>(StringComparer.OrdinalIgnoreCase);
            }

            await using var stream = File.OpenRead(path);
            var payload = await JsonSerializer.DeserializeAsync<Dictionary<string, CacheEntry>>(stream)
                          ?? new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            return payload
                .Where(x => x.Value is not null)
                .ToDictionary(
                    x => x.Key,
                    x => x.Value!.ToCachedResult(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, CachedGeocodingResult>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            CacheGate.Release();
        }
    }

    private static async Task TrySaveCacheEntryAsync(string? cacheFilePath, string key, CachedGeocodingResult result)
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

            payload[key] = CacheEntry.FromCachedResult(result);

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
        public string MatchType { get; set; } = "Cached";
        public string? EntityType { get; set; }
        public bool? IsPrecise { get; set; }

        public CachedGeocodingResult ToCachedResult()
        {
            return new CachedGeocodingResult(
                new GeoPoint(Latitude, Longitude),
                string.IsNullOrWhiteSpace(MatchType) ? "Cached" : MatchType,
                EntityType,
                IsPrecise ?? false);
        }

        public static CacheEntry FromCachedResult(CachedGeocodingResult result)
        {
            return new CacheEntry(result.Location.Latitude, result.Location.Longitude)
            {
                MatchType = result.MatchType,
                EntityType = result.EntityType,
                IsPrecise = result.IsPrecise
            };
        }
    }

    private sealed record GeocodeCandidate(GeoPoint Point, string Type, string? EntityType, string Query);
    private sealed record CachedGeocodingResult(GeoPoint Location, string MatchType, string? EntityType, bool IsPrecise)
    {
        public bool Matches(AddressGeocodingResult result)
        {
            return Location == result.Location &&
                   string.Equals(MatchType, result.MatchType, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(EntityType ?? string.Empty, result.EntityType ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   IsPrecise == result.IsPrecise;
        }
    }
}

public sealed record AddressGeocodingResult(
    GeoPoint Location,
    bool IsPrecise,
    string Query,
    string MatchType,
    string? EntityType);
