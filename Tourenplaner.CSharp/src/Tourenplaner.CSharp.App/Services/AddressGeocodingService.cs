using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class AddressGeocodingService
{
    private const double SwitzerlandCenterLat = 46.798562;
    private const double SwitzerlandCenterLon = 8.231974;
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(1100);
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTime _lastRequestUtc = DateTime.MinValue;

    public static async Task<GeoPoint?> TryGeocodeOrderAsync(Order order)
    {
        var street = (order.DeliveryAddress?.Street ?? string.Empty).Trim();
        var postalCode = (order.DeliveryAddress?.PostalCode ?? string.Empty).Trim();
        var city = (order.DeliveryAddress?.City ?? string.Empty).Trim();
        var fallback = (order.Address ?? string.Empty).Trim();
        return await TryGeocodeAddressAsync(street, postalCode, city, fallback);
    }

    public static async Task<GeoPoint?> TryGeocodeAddressAsync(string? street, string? postalCode, string? city, string? fallbackAddress = null)
    {
        var queries = BuildQueries(
            (street ?? string.Empty).Trim(),
            (postalCode ?? string.Empty).Trim(),
            (city ?? string.Empty).Trim(),
            (fallbackAddress ?? string.Empty).Trim());

        foreach (var query in queries)
        {
            var result = await TryGeocodeQueryAsync(query);
            if (result is not null)
            {
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

    private static async Task<GeoPoint?> TryGeocodeQueryAsync(string query)
    {
        var uri = $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q={Uri.EscapeDataString(query)}";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var response = await SendThrottledAsync(uri);
                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode == 429 && attempt == 0)
                    {
                        await Task.Delay(2000);
                        continue;
                    }

                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                var payload = await JsonSerializer.DeserializeAsync<List<NominatimSearchItem>>(stream);
                var first = payload?.FirstOrDefault();
                if (first is null)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(first.addresstype) &&
                    string.Equals(first.addresstype, "country", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (first.place_rank is <= 8 and > 0)
                {
                    return null;
                }

                if (!double.TryParse(first.lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
                {
                    return null;
                }

                if (!double.TryParse(first.lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                {
                    return null;
                }

                return new GeoPoint(lat, lon);
            }
            catch
            {
                if (attempt == 0)
                {
                    await Task.Delay(700);
                    continue;
                }
            }
        }

        return null;
    }

    private static async Task<HttpResponseMessage> SendThrottledAsync(string uri)
    {
        await RequestGate.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            var wait = MinRequestInterval - (now - _lastRequestUtc);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait);
            }

            _lastRequestUtc = DateTime.UtcNow;
            return await Client.GetAsync(uri);
        }
        finally
        {
            RequestGate.Release();
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

    private sealed class NominatimSearchItem
    {
        public string lat { get; set; } = string.Empty;
        public string lon { get; set; } = string.Empty;
        public string addresstype { get; set; } = string.Empty;
        public int place_rank { get; set; }
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
}
