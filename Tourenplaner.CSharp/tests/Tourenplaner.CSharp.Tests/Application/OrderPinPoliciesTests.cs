using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderPinPoliciesTests
{
    [Fact]
    public void ValidationPolicy_AcceptsUnchangedAddressWithoutGeocoding()
    {
        var existing = CreateOrder("Seewenstrasse", "130", "6423", "Seewen");
        var updated = CreateOrder(" SEEWENSTRASSE ", "130", "6423", "Séewen");

        Assert.False(OrderPinValidationPolicy.RequiresGeocoding(existing, updated));
        Assert.Equal(
            OrderPinValidationDecision.AcceptWithoutGeocoding,
            OrderPinValidationPolicy.ResolveDecision(existing, updated, geocodingResult: null));
    }

    [Fact]
    public void ValidationPolicy_AppliesPreciseGeocodingWithoutUiState()
    {
        var existing = CreateOrder("Altstrasse", "1", "8000", "Zürich");
        var updated = CreateOrder("Neustrasse", "2", "8000", "Zürich");
        var geocodingResult = CreateGeocodingResult(isPrecise: true);

        var decision = OrderPinValidationPolicy.ResolveDecision(existing, updated, geocodingResult);
        OrderPinValidationPolicy.ApplyPreciseGeocoding(updated, geocodingResult);

        Assert.Equal(OrderPinValidationDecision.AcceptPreciseGeocoding, decision);
        Assert.Equal(geocodingResult.Location, updated.Location);
        Assert.False(updated.IsLocationManuallySet);
        Assert.Empty(updated.ManualLocationAddress);
        Assert.False(updated.ManualLocationRequiresReview);
    }

    [Fact]
    public void ValidationPolicy_PreservesExistingManualLocationForConfirmation()
    {
        var existing = CreateOrder("Altstrasse", "1", "8000", "Zürich");
        existing.IsLocationManuallySet = true;
        existing.Location = new GeoPoint(47.1, 8.1);
        var updated = CreateOrder("Neustrasse", "2", "8000", "Zürich");

        var confirmation = OrderPinValidationPolicy.CreateManualConfirmation(
            existing,
            updated,
            CreateGeocodingResult(isPrecise: false));

        Assert.Equal(existing.Location, confirmation.InitialLocation);
        Assert.True(confirmation.ExistingLocationIsManual);
        Assert.False(confirmation.RequireManualPlacement);
        Assert.Equal("Neustrasse 2, 8000 Zürich", confirmation.AddressLine);
    }

    [Fact]
    public void AssignmentWarningPolicy_ReturnsNoWarningForPreciseResult()
    {
        var order = CreateOrder("Neustrasse", "2", "8000", "Zürich");

        var warning = OrderPinAssignmentWarningPolicy.Create(
            order,
            CreateGeocodingResult(isPrecise: true));

        Assert.Null(warning);
    }

    [Fact]
    public void AssignmentWarningPolicy_UsesFallbackAddressWhenDeliveryAddressIsEmpty()
    {
        var order = CreateOrder(string.Empty, string.Empty, string.Empty, string.Empty);
        order.Id = "A-1";
        order.Address = "Fallbackstrasse 5, 8000 Zürich";

        var warning = OrderPinAssignmentWarningPolicy.Create(order, geocodingResult: null);

        Assert.NotNull(warning);
        Assert.Equal("Pin prüfen", warning.Title);
        Assert.Contains("Auftrag A-1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Adresse: Fallbackstrasse 5, 8000 Zürich", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssignmentWarningPolicy_IncludesApproximateMatchDetails()
    {
        var order = CreateOrder("Neustrasse", "2", "8000", "Zürich");
        var result = CreateGeocodingResult(isPrecise: false) with
        {
            MatchType = "Fuzzy",
            EntityType = "Municipality"
        };

        var warning = OrderPinAssignmentWarningPolicy.Create(order, result);

        Assert.NotNull(warning);
        Assert.Contains("Trefferart: Fuzzy / Municipality", warning.Message, StringComparison.Ordinal);
    }

    private static Order CreateOrder(string street, string houseNumber, string postalCode, string city) =>
        new()
        {
            DeliveryType = "Frei Bordsteinkante",
            DeliveryAddress = new DeliveryAddressInfo
            {
                Street = street,
                HouseNumber = houseNumber,
                PostalCode = postalCode,
                City = city
            }
        };

    private static AddressGeocodingResult CreateGeocodingResult(bool isPrecise) =>
        new(
            new GeoPoint(47.2, 8.2),
            isPrecise,
            "Neustrasse 2, 8000 Zürich",
            "Address",
            "Address");
}
