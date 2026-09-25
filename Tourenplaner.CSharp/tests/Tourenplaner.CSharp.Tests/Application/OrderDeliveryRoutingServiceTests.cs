using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderDeliveryRoutingServiceTests
{
    [Theory]
    [InlineData(DeliveryMethodExtensions.Spediteur, true, "Spediteur / Gawela")]
    [InlineData(DeliveryMethodExtensions.MitVerteilung, true, "Gawela / Spediteur")]
    [InlineData(DeliveryMethodExtensions.Spediteur, false, DeliveryMethodExtensions.Spediteur)]
    [InlineData(DeliveryMethodExtensions.MitVerteilung, false, DeliveryMethodExtensions.MitVerteilung)]
    public void PlanningDeliveryDisplayLabel_ReflectsEnabledAlternative(
        string deliveryType,
        bool isAlternativeDeliveryEnabled,
        string expected)
    {
        var order = new Order
        {
            DeliveryType = deliveryType,
            IsAlternativeDeliveryEnabled = isAlternativeDeliveryEnabled
        };

        Assert.Equal(expected, DeliveryMethodExtensions.GetPlanningDeliveryDisplayLabel(order));
    }

    [Fact]
    public async Task SpediteurWithAlternativeLiefertour_IsGeocodedAndKeepsTourAssignment()
    {
        var order = new Order
        {
            DeliveryType = DeliveryMethodExtensions.Spediteur,
            IsAlternativeDeliveryEnabled = true,
            AssignedTourId = "7"
        };
        var expectedLocation = new GeoPoint(47.4, 8.5);

        var result = await OrderDeliveryRoutingService.ApplyAsync(
            order,
            fallbackLocation: null,
            _ => Task.FromResult<AddressGeocodingResult?>(new AddressGeocodingResult(
                expectedLocation, true, "query", "Point Address", "Point Address")));

        Assert.NotNull(result);
        Assert.Equal(OrderType.NonMap, order.Type);
        Assert.Equal(expectedLocation, order.Location);
        Assert.Equal("7", order.AssignedTourId);
    }

    [Fact]
    public async Task SpediteurWithoutAlternative_ClearsMapStateWithoutGeocoding()
    {
        var order = new Order
        {
            DeliveryType = DeliveryMethodExtensions.Spediteur,
            IsAlternativeDeliveryEnabled = false,
            Location = new GeoPoint(47.4, 8.5),
            AssignedTourId = "7"
        };
        var geocodingCalled = false;

        var result = await OrderDeliveryRoutingService.ApplyAsync(
            order,
            fallbackLocation: null,
            _ =>
            {
                geocodingCalled = true;
                return Task.FromResult<AddressGeocodingResult?>(null);
            });

        Assert.Null(result);
        Assert.False(geocodingCalled);
        Assert.Null(order.Location);
        Assert.True(string.IsNullOrWhiteSpace(order.AssignedTourId));
    }
}
