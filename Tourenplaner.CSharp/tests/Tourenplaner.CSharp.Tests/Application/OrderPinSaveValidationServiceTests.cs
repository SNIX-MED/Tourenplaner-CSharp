using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderPinSaveValidationServiceTests
{
    [Fact]
    public void HasDeliveryAddressChanged_IgnoresFormattingCaseAndDiacritics()
    {
        var existing = CreateOrder("Seewenstrasse", "130", "6423", "Seewen");
        var updated = CreateOrder("  SEEWENSTRASSE ", "130", "6423", "Séewen");

        Assert.False(OrderPinSaveValidationService.HasDeliveryAddressChanged(existing, updated));
    }

    [Fact]
    public void HasDeliveryAddressChanged_TreatsStrasseAndSharpSAsEquivalent()
    {
        var existing = CreateOrder("Musterstrasse", "1", "8000", "Zürich");
        var updated = CreateOrder("Musterstraße", "1", "8000", "Zurich");

        Assert.False(OrderPinSaveValidationService.HasDeliveryAddressChanged(existing, updated));
    }

    [Theory]
    [InlineData("St. Gallen", "Sankt Gallen")]
    [InlineData("St Gallen", "Sankt Gallen")]
    [InlineData("St. Moritz", "Sankt Moritz")]
    public void HasDeliveryAddressChanged_TreatsStAndSanktAsEquivalent(string firstCity, string secondCity)
    {
        var existing = CreateOrder("Bahnhofstrasse", "1", "9000", firstCity);
        var updated = CreateOrder("Bahnhofstrasse", "1", "9000", secondCity);

        Assert.False(OrderPinSaveValidationService.HasDeliveryAddressChanged(existing, updated));
    }

    [Fact]
    public void SwissAddressNormalization_DoesNotChangeStInsideOtherWords()
    {
        Assert.Equal("stgallenkappel", SwissAddressNormalization.NormalizeForComparison("StGallenkappel"));
        Assert.Equal("ostermundigen", SwissAddressNormalization.NormalizeForComparison("Ostermundigen"));
    }

    [Theory]
    [InlineData("Neue Strasse", "1", "8000", "Zürich")]
    [InlineData("Musterstrasse", "2", "8000", "Zürich")]
    [InlineData("Musterstrasse", "1", "8001", "Zürich")]
    [InlineData("Musterstrasse", "1", "8000", "Bern")]
    public void HasDeliveryAddressChanged_DetectsMaterialChanges(
        string street, string houseNumber, string postalCode, string city)
    {
        var existing = CreateOrder("Musterstrasse", "1", "8000", "Zürich");
        var updated = CreateOrder(street, houseNumber, postalCode, city);

        Assert.True(OrderPinSaveValidationService.HasDeliveryAddressChanged(existing, updated));
    }

    private static Order CreateOrder(string street, string houseNumber, string postalCode, string city) => new()
    {
        DeliveryAddress = new DeliveryAddressInfo
        {
            Street = street,
            HouseNumber = houseNumber,
            PostalCode = postalCode,
            City = city
        }
    };
}
