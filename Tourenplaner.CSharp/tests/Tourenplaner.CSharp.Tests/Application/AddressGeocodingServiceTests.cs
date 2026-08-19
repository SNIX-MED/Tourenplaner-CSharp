using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class AddressGeocodingServiceTests
{
    [Fact]
    public async Task TryGeocodeOrderAsync_IgnoresLegacyPreciseCacheEntryWithoutAddressValidation()
    {
        var cacheFilePath = Path.Combine(Path.GetTempPath(), $"gawela-geocode-cache-{Guid.NewGuid():N}.json");
        try
        {
            var cache = new Dictionary<string, object>
            {
                ["badenerstrasse 378a, 8004 zürich, schweiz"] = new
                {
                    Latitude = 47.0248595,
                    Longitude = 8.2978893,
                    MatchType = "Point Address",
                    EntityType = (string?)null,
                    IsPrecise = true
                }
            };
            await File.WriteAllTextAsync(cacheFilePath, JsonSerializer.Serialize(cache));

            var order = CreateMapOrder(
                "221868",
                "Badenerstrasse",
                "378A",
                "8004",
                "Zürich");

            var location = await AddressGeocodingService.TryGeocodeOrderAsync(order, tomTomApiKey: null, cacheFilePath);

            Assert.Null(location);
        }
        finally
        {
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
            }
        }
    }

    [Fact]
    public async Task TryResolveOrderAsync_DoesNotUseRawInMemoryCacheWhenResolutionMetadataIsRejected()
    {
        var query = "Industriestrasse 6, 9015 St.Gallen, Schweiz";
        var key = query.ToLowerInvariant();
        var wrongLocation = new GeoPoint(46.798562, 8.231974);

        ClearGeocodingMemoryCaches();
        SeedGeocodingMemoryCache(
            key,
            wrongLocation,
            new AddressGeocodingResult(
                wrongLocation,
                true,
                query,
                "Point Address",
                null,
                "9999",
                "Bern",
                "Industriestrasse",
                "Industriestrasse 6, 9999 Bern",
                1));

        try
        {
            var order = CreateMapOrder("221700", "Industriestrasse", "6", "9015", "St.Gallen");

            var result = await AddressGeocodingService.TryResolveOrderAsync(order, tomTomApiKey: null);

            Assert.Null(result);
        }
        finally
        {
            ClearGeocodingMemoryCaches();
        }
    }

    [Fact]
    public async Task TryResolveOrderAsync_AcceptsPreciseCacheWhenMunicipalityDiffersButFreeformMatchesCity()
    {
        var cacheFilePath = Path.Combine(Path.GetTempPath(), $"gawela-geocode-cache-{Guid.NewGuid():N}.json");
        try
        {
            var expectedLocation = new GeoPoint(47.0301, 8.6338);
            var cache = new Dictionary<string, object>
            {
                ["hauptmatt 9, 6423 seewen, schweiz"] = new
                {
                    Latitude = expectedLocation.Latitude,
                    Longitude = expectedLocation.Longitude,
                    MatchType = "Point Address",
                    EntityType = (string?)null,
                    IsPrecise = true,
                    ResultPostalCode = "6423",
                    ResultMunicipality = "Schwyz",
                    ResultStreetName = "Hauptmatt",
                    ResultFreeformAddress = "Hauptmatt 9, 6423 Seewen SZ",
                    CacheValidationVersion = 1
                }
            };
            await File.WriteAllTextAsync(cacheFilePath, JsonSerializer.Serialize(cache));

            var order = CreateMapOrder("221680", "Hauptmatt", "9", "6423", "Seewen");

            var result = await AddressGeocodingService.TryResolveOrderAsync(order, tomTomApiKey: null, cacheFilePath);

            Assert.NotNull(result);
            Assert.True(result.IsPrecise);
            Assert.Equal(expectedLocation, result.Location);
        }
        finally
        {
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
            }
        }
    }

    [Fact]
    public void ClearSuspiciousSharedOrderLocations_ClearsSameCoordinateAcrossDifferentPostalCities()
    {
        var sharedLocation = new GeoPoint(47.0248595, 8.2978893);
        var orders = new List<Order>
        {
            CreateMapOrder("221751", "Schweighofstrasse", "14", "6010", "Kriens", sharedLocation),
            CreateMapOrder("221803", "Thurgauerstrasse", "39", "8050", "Zürich", sharedLocation),
            CreateMapOrder("OK1", "Nüschelerstrasse", "22", "8001", "Zürich", new GeoPoint(47.3720041, 8.536779))
        };

        var cleared = AddressGeocodingService.ClearSuspiciousSharedOrderLocations(orders);

        Assert.Equal(2, cleared);
        Assert.Null(orders[0].Location);
        Assert.Null(orders[1].Location);
        Assert.NotNull(orders[2].Location);
    }

    private static Order CreateMapOrder(
        string id,
        string street,
        string houseNumber,
        string postalCode,
        string city,
        GeoPoint? location = null)
    {
        return new Order
        {
            Id = id,
            Type = OrderType.Map,
            CustomerName = $"Kunde {id}",
            Location = location,
            DeliveryAddress = new DeliveryAddressInfo
            {
                Street = street,
                HouseNumber = houseNumber,
                PostalCode = postalCode,
                City = city
            }
        };
    }

    private static void SeedGeocodingMemoryCache(
        string key,
        GeoPoint location,
        AddressGeocodingResult resolution)
    {
        GetInMemoryLocationCache()[key] = location;
        GetInMemoryResolutionCache()[key] = resolution;
    }

    private static void ClearGeocodingMemoryCaches()
    {
        GetInMemoryLocationCache().Clear();
        GetInMemoryResolutionCache().Clear();
    }

    private static ConcurrentDictionary<string, GeoPoint> GetInMemoryLocationCache()
    {
        return GetPrivateStaticField<ConcurrentDictionary<string, GeoPoint>>("InMemoryCache");
    }

    private static ConcurrentDictionary<string, AddressGeocodingResult> GetInMemoryResolutionCache()
    {
        return GetPrivateStaticField<ConcurrentDictionary<string, AddressGeocodingResult>>("InMemoryResolutionCache");
    }

    private static T GetPrivateStaticField<T>(string fieldName)
        where T : class
    {
        var field = typeof(AddressGeocodingService).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field.GetValue(null);
        return Assert.IsType<T>(value);
    }
}
