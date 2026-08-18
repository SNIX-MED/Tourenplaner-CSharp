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
}
