using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.IO;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class AddressGeocodingService
{
    private const double SwitzerlandCenterLat = 46.798562;
    private const double SwitzerlandCenterLon = 8.231974;
    private const int CurrentCacheValidationVersion = 1;
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim CacheGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, GeoPoint> InMemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, AddressGeocodingResult> InMemoryResolutionCache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<GeoPoint?> TryGeocodeOrderAsync(Order order, string? tomTomApiKey = null, string? cacheFilePath = null)
    {
        var result = await TryResolveOrderAsync(order, tomTomApiKey, cacheFilePath);
        return result?.IsPrecise == true ? result.Location : null;
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
        var expectation = new AddressExpectation(
            (street ?? string.Empty).Trim(),
            (postalCode ?? string.Empty).Trim(),
            (city ?? string.Empty).Trim());
        var queries = BuildQueries(
            expectation.Street,
            expectation.PostalCode,
            expectation.City,
            (fallbackAddress ?? string.Empty).Trim());

        var persistedCache = await TryLoadCacheFromFileAsync(cacheFilePath);
        var canResolveWithTomTom = !string.IsNullOrWhiteSpace(tomTomApiKey);
        GeocodeCandidate? bestCandidate = null;

        foreach (var query in queries)
        {
            var key = NormalizeWhitespace(query).ToLowerInvariant();

            GeocodeCandidate? candidate = null;
            GeocodeCandidate? cachedFallback = null;
            if (TryGetCachedResolution(key, out var cachedResolution) && cachedResolution is not null)
            {
                if (IsCachedResolutionUsable(cachedResolution, expectation))
                {
                    var cachedCandidate = GeocodeCandidate.FromResult(cachedResolution, query);
                    if (cachedResolution.IsPrecise || !canResolveWithTomTom)
                    {
                        candidate = cachedCandidate;
                    }
                    else
                    {
                        cachedFallback = cachedCandidate;
                    }
                }
            }
            else if (!canResolveWithTomTom && TryGetCachedLocation(key, out var cached) && cached is not null)
            {
                candidate = new GeocodeCandidate(cached, "Cached", null, query, null, null, null, null, null);
            }

            if (candidate is null && persistedCache.TryGetValue(key, out var persisted))
            {
                var persistedResult = new AddressGeocodingResult(
                    persisted.Location,
                    persisted.IsPrecise,
                    query,
                    persisted.MatchType,
                    persisted.EntityType,
                    persisted.ResultPostalCode,
                    persisted.ResultMunicipality,
                    persisted.ResultStreetName,
                    persisted.ResultFreeformAddress,
                    persisted.CacheValidationVersion);

                if (IsCachedResolutionUsable(persistedResult, expectation))
                {
                    InMemoryCache[key] = persisted.Location;
                    InMemoryResolutionCache[key] = persistedResult;
                    var persistedCandidate = GeocodeCandidate.FromResult(persistedResult, query);
                    if (persistedResult.IsPrecise || !canResolveWithTomTom)
                    {
                        candidate = persistedCandidate;
                    }
                    else
                    {
                        cachedFallback ??= persistedCandidate;
                    }
                }
            }

            if (candidate is null)
            {
                candidate = await TryGeocodeQueryAsync(query, tomTomApiKey, expectation);
                if (candidate is not null)
                {
                    InMemoryCache[key] = candidate.Point;
                    var resolution = CreateResolution(candidate, expectation);
                    InMemoryResolutionCache[key] = resolution;
                    persistedCache[key] = new CachedGeocodingResult(
                        candidate.Point,
                        resolution.MatchType,
                        resolution.EntityType,
                        resolution.IsPrecise,
                        resolution.ResultPostalCode,
                        resolution.ResultMunicipality,
                        resolution.ResultStreetName,
                        resolution.ResultFreeformAddress,
                        resolution.CacheValidationVersion);
                    await TrySaveCacheEntryAsync(cacheFilePath, key, persistedCache[key]);
                }
                else
                {
                    candidate = cachedFallback;
                }
            }

            if (candidate is null)
            {
                continue;
            }

            if (IsBetterCandidate(candidate, bestCandidate, expectation))
            {
                bestCandidate = candidate;
            }

            if (IsExactAddressCandidate(candidate, expectation))
            {
                break;
            }
        }

        if (bestCandidate is null)
        {
            return null;
        }

        return CreateResolution(bestCandidate, expectation);
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

    private static async Task<GeocodeCandidate?> TryGeocodeQueryAsync(
        string query,
        string? tomTomApiKey,
        AddressExpectation expectation)
    {
        var key = (tomTomApiKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return await TryGeocodeWithTomTomAsync(query, key, expectation);
    }

    private static async Task<GeocodeCandidate?> TryGeocodeWithTomTomAsync(
        string query,
        string apiKey,
        AddressExpectation expectation)
    {
        var uri = $"https://api.tomtom.com/search/2/geocode/{Uri.EscapeDataString(query)}.json?key={Uri.EscapeDataString(apiKey)}&limit=5&countrySet=CH";
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

            GeocodeCandidate? bestCandidate = null;
            foreach (var result in results.EnumerateArray())
            {
                var position = result.TryGetProperty("position", out var pos) ? pos : default;
                if (position.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!position.TryGetProperty("lat", out var latElement) || latElement.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                if (!position.TryGetProperty("lon", out var lonElement) || lonElement.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var type = result.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()
                    : string.Empty;
                var entityType = result.TryGetProperty("entityType", out var entityTypeElement) && entityTypeElement.ValueKind == JsonValueKind.String
                    ? entityTypeElement.GetString()
                    : null;
                var address = result.TryGetProperty("address", out var addressElement) && addressElement.ValueKind == JsonValueKind.Object
                    ? addressElement
                    : default;
                var resultPostalCode = ReadJsonString(address, "postalCode");
                var resultMunicipality = ReadJsonString(address, "municipality");
                var resultStreetName = ReadJsonString(address, "streetName");
                var resultFreeformAddress = ReadJsonString(address, "freeformAddress");

                var candidate = new GeocodeCandidate(
                    new GeoPoint(latElement.GetDouble(), lonElement.GetDouble()),
                    type ?? string.Empty,
                    entityType,
                    query,
                    resultPostalCode,
                    resultMunicipality,
                    resultStreetName,
                    resultFreeformAddress,
                    CurrentCacheValidationVersion);

                if (IsBetterCandidate(candidate, bestCandidate, expectation))
                {
                    bestCandidate = candidate;
                }

                if (IsExactAddressCandidate(candidate, expectation))
                {
                    break;
                }
            }

            return bestCandidate;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
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

    private static AddressGeocodingResult CreateResolution(GeocodeCandidate candidate, AddressExpectation expectation)
    {
        var normalizedType = NormalizeWhitespace(candidate.Type);
        var hasPreciseType =
            string.Equals(normalizedType, "Point Address", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedType, "Address Range", StringComparison.OrdinalIgnoreCase);
        var isPrecise = hasPreciseType && IsCandidateConsistentWithExpectedAddress(candidate, expectation);

        return new AddressGeocodingResult(
            candidate.Point,
            isPrecise,
            candidate.Query,
            candidate.Type,
            candidate.EntityType,
            candidate.ResultPostalCode,
            candidate.ResultMunicipality,
            candidate.ResultStreetName,
            candidate.ResultFreeformAddress,
            candidate.CacheValidationVersion);
    }

    private static bool IsBetterCandidate(
        GeocodeCandidate candidate,
        GeocodeCandidate? bestCandidate,
        AddressExpectation expectation)
    {
        if (bestCandidate is null)
        {
            return true;
        }

        var candidateScore = GetCandidateScore(candidate, expectation);
        var bestScore = GetCandidateScore(bestCandidate, expectation);
        if (candidateScore != bestScore)
        {
            return candidateScore > bestScore;
        }

        return GetQuerySpecificityScore(candidate.Query) > GetQuerySpecificityScore(bestCandidate.Query);
    }

    private static bool IsExactAddressCandidate(GeocodeCandidate candidate, AddressExpectation expectation)
    {
        return string.Equals(
            NormalizeWhitespace(candidate.Type),
            "Point Address",
            StringComparison.OrdinalIgnoreCase) &&
            IsCandidateConsistentWithExpectedAddress(candidate, expectation);
    }

    private static bool IsCachedResolutionUsable(AddressGeocodingResult cached, AddressExpectation expectation)
    {
        if (!HasMeaningfulAddressExpectation(expectation))
        {
            return true;
        }

        return cached.CacheValidationVersion >= CurrentCacheValidationVersion &&
               IsCandidateConsistentWithExpectedAddress(GeocodeCandidate.FromResult(cached, cached.Query), expectation);
    }

    private static bool IsCandidateConsistentWithExpectedAddress(GeocodeCandidate candidate, AddressExpectation expectation)
    {
        if (!HasMeaningfulAddressExpectation(expectation))
        {
            return true;
        }

        if (candidate.CacheValidationVersion < CurrentCacheValidationVersion)
        {
            return false;
        }

        var expectedPostalCode = NormalizeDigits(expectation.PostalCode);
        if (!string.IsNullOrWhiteSpace(expectedPostalCode))
        {
            var resultPostalCode = NormalizeDigits(candidate.ResultPostalCode);
            if (string.IsNullOrWhiteSpace(resultPostalCode) ||
                !string.Equals(expectedPostalCode, resultPostalCode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var expectedCity = NormalizeAddressToken(expectation.City);
        if (!string.IsNullOrWhiteSpace(expectedCity))
        {
            var resultMunicipality = NormalizeAddressToken(candidate.ResultMunicipality);
            var resultFreeform = NormalizeAddressToken(candidate.ResultFreeformAddress);
            if (string.IsNullOrWhiteSpace(resultMunicipality) &&
                string.IsNullOrWhiteSpace(resultFreeform))
            {
                return false;
            }

            if (!AddressTokenContains(resultMunicipality, expectedCity) &&
                !AddressTokenContains(resultFreeform, expectedCity))
            {
                return false;
            }
        }

        var expectedStreet = NormalizeAddressToken(RemoveHouseNumber(expectation.Street));
        if (!string.IsNullOrWhiteSpace(expectedStreet))
        {
            var resultStreetName = NormalizeAddressToken(candidate.ResultStreetName);
            var resultFreeform = NormalizeAddressToken(candidate.ResultFreeformAddress);
            if (string.IsNullOrWhiteSpace(resultStreetName) &&
                string.IsNullOrWhiteSpace(resultFreeform))
            {
                return false;
            }

            if (!AddressTokenContains(resultStreetName, expectedStreet) &&
                !AddressTokenContains(resultFreeform, expectedStreet))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasMeaningfulAddressExpectation(AddressExpectation expectation)
    {
        return !string.IsNullOrWhiteSpace(expectation.Street) ||
               !string.IsNullOrWhiteSpace(expectation.PostalCode) ||
               !string.IsNullOrWhiteSpace(expectation.City);
    }

    private static string NormalizeDigits(string? value)
    {
        return string.Concat((value ?? string.Empty).Where(char.IsDigit));
    }

    private static string RemoveHouseNumber(string value)
    {
        return Regex.Replace(value ?? string.Empty, @"\b\d+[a-zA-Z]?\b", " ");
    }

    private static bool AddressTokenContains(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
        {
            return false;
        }

        return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               needle.Contains(haystack, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAddressToken(string? value)
    {
        var normalized = NormalizeWhitespace(value ?? string.Empty)
            .Replace("ä", "a", StringComparison.OrdinalIgnoreCase)
            .Replace("ö", "o", StringComparison.OrdinalIgnoreCase)
            .Replace("ü", "u", StringComparison.OrdinalIgnoreCase)
            .Replace("ß", "ss", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var decomposed = normalized.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
            }
        }

        return NormalizeWhitespace(builder.ToString());
    }

    private static int GetCandidateScore(GeocodeCandidate candidate, AddressExpectation expectation)
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

        var score = baseScore + GetQuerySpecificityScore(candidate.Query);
        if (IsCandidateConsistentWithExpectedAddress(candidate, expectation))
        {
            score += 1_000;
        }

        if (IsExactAddressCandidate(candidate, expectation))
        {
            score += 3_000;
        }

        return score;
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

    public static int ClearSuspiciousSharedOrderLocations(IList<Order> orders)
    {
        if (orders.Count == 0)
        {
            return 0;
        }

        var suspiciousGroups = orders
            .Where(x => x.Type == OrderType.Map && x.Location is not null)
            .GroupBy(x => BuildCoordinateKey(x.Location!))
            .Where(group =>
            {
                if (group.Count() < 2)
                {
                    return false;
                }

                return group
                    .Select(BuildPostalCityKey)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() > 1;
            })
            .ToList();

        var cleared = 0;
        foreach (var order in suspiciousGroups.SelectMany(x => x))
        {
            if (order.Location is null)
            {
                continue;
            }

            order.Location = null;
            cleared++;
        }

        return cleared;
    }

    private static string BuildCoordinateKey(GeoPoint point)
    {
        return $"{point.Latitude.ToString("0.000000", CultureInfo.InvariantCulture)}|" +
               $"{point.Longitude.ToString("0.000000", CultureInfo.InvariantCulture)}";
    }

    private static string BuildPostalCityKey(Order order)
    {
        return NormalizeAddressToken(string.Join(" ", new[]
        {
            order.DeliveryAddress?.PostalCode,
            order.DeliveryAddress?.City
        }.Where(x => !string.IsNullOrWhiteSpace(x))));
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
        public string? ResultPostalCode { get; set; }
        public string? ResultMunicipality { get; set; }
        public string? ResultStreetName { get; set; }
        public string? ResultFreeformAddress { get; set; }
        public int? CacheValidationVersion { get; set; }

        public CachedGeocodingResult ToCachedResult()
        {
            return new CachedGeocodingResult(
                new GeoPoint(Latitude, Longitude),
                string.IsNullOrWhiteSpace(MatchType) ? "Cached" : MatchType,
                EntityType,
                IsPrecise ?? false,
                ResultPostalCode,
                ResultMunicipality,
                ResultStreetName,
                ResultFreeformAddress,
                CacheValidationVersion);
        }

        public static CacheEntry FromCachedResult(CachedGeocodingResult result)
        {
            return new CacheEntry(result.Location.Latitude, result.Location.Longitude)
            {
                MatchType = result.MatchType,
                EntityType = result.EntityType,
                IsPrecise = result.IsPrecise,
                ResultPostalCode = result.ResultPostalCode,
                ResultMunicipality = result.ResultMunicipality,
                ResultStreetName = result.ResultStreetName,
                ResultFreeformAddress = result.ResultFreeformAddress,
                CacheValidationVersion = result.CacheValidationVersion
            };
        }
    }

    private sealed record AddressExpectation(string Street, string PostalCode, string City);

    private sealed record GeocodeCandidate(
        GeoPoint Point,
        string Type,
        string? EntityType,
        string Query,
        string? ResultPostalCode,
        string? ResultMunicipality,
        string? ResultStreetName,
        string? ResultFreeformAddress,
        int? CacheValidationVersion)
    {
        public static GeocodeCandidate FromResult(AddressGeocodingResult result, string query)
        {
            return new GeocodeCandidate(
                result.Location,
                result.MatchType,
                result.EntityType,
                query,
                result.ResultPostalCode,
                result.ResultMunicipality,
                result.ResultStreetName,
                result.ResultFreeformAddress,
                result.CacheValidationVersion);
        }
    }

    private sealed record CachedGeocodingResult(
        GeoPoint Location,
        string MatchType,
        string? EntityType,
        bool IsPrecise,
        string? ResultPostalCode,
        string? ResultMunicipality,
        string? ResultStreetName,
        string? ResultFreeformAddress,
        int? CacheValidationVersion)
    {
        public bool Matches(AddressGeocodingResult result)
        {
            return Location == result.Location &&
                   string.Equals(MatchType, result.MatchType, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(EntityType ?? string.Empty, result.EntityType ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   IsPrecise == result.IsPrecise &&
                   string.Equals(ResultPostalCode ?? string.Empty, result.ResultPostalCode ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ResultMunicipality ?? string.Empty, result.ResultMunicipality ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ResultStreetName ?? string.Empty, result.ResultStreetName ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ResultFreeformAddress ?? string.Empty, result.ResultFreeformAddress ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   CacheValidationVersion == result.CacheValidationVersion;
        }
    }
}

public sealed record AddressGeocodingResult(
    GeoPoint Location,
    bool IsPrecise,
    string Query,
    string MatchType,
    string? EntityType,
    string? ResultPostalCode = null,
    string? ResultMunicipality = null,
    string? ResultStreetName = null,
    string? ResultFreeformAddress = null,
    int? CacheValidationVersion = null);
