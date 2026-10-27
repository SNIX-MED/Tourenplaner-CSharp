using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderPinPoliciesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preview_UsesNewSearchResultUnlessStoredPinWasManuallyConfirmed(bool manual)
    {
        var order = CreateOrder("Solistrasse", "1", "8180", "Bülach");
        order.Location = new GeoPoint(47.5246046, 8.5373749);
        order.IsLocationManuallySet = manual;
        var result = CreateGeocodingResult(true);
        Assert.Equal(manual ? order.Location : result.Location,
            OrderPinValidationPolicy.ResolvePreviewLocation(order, result));
        Assert.Equal(order.Location, OrderPinValidationPolicy.ResolvePreviewLocation(order, null));
    }

    [Theory]
    [InlineData(AddressGeocodingFailureReason.Timeout)]
    [InlineData(AddressGeocodingFailureReason.ConnectionFailed)]
    [InlineData(AddressGeocodingFailureReason.RateLimited)]
    [InlineData(AddressGeocodingFailureReason.NoResult)]
    public void Import_PreservesExistingLocationWhenLookupFails(AddressGeocodingFailureReason reason)
    {
        var order = CreateOrder("Unterwilrain", "16", "6014", "Luzern");
        order.Location = new GeoPoint(47.0491152, 8.2713488);
        var resolution = new AddressGeocodingResolution(null, reason);

        Assert.Equal(order.Location, OrderPinValidationPolicy.ResolveImportedLocation(order, resolution.Result));
    }

    [Fact]
    public void Import_DoesNotInventLocationWhenLookupFails()
    {
        var order = CreateOrder("Solistrasse", "1", "8180", "Bülach");
        order.Location = null;
        Assert.Null(OrderPinValidationPolicy.ResolveImportedLocation(order, null));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Import_OnlyReplacesAutomaticLocationWithPreciseResult(bool precise, bool manual)
    {
        var order = CreateOrder("Solistrasse", "1", "8180", "Bülach");
        order.Location = new GeoPoint(47.5246046, 8.5373749);
        order.IsLocationManuallySet = manual;
        var result = CreateGeocodingResult(precise);

        Assert.Equal(precise && !manual ? result.Location : order.Location,
            OrderPinValidationPolicy.ResolveImportedLocation(order, result));
    }

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
    public void AssignmentWarningPolicy_ReturnsNoWarningForConfirmedManualPinWithoutLookup()
    {
        var order = CreateOrder("Via Principala", string.Empty, "7031", "Laax");
        order.Location = new GeoPoint(46.806, 9.258);
        order.IsLocationManuallySet = true;
        order.ManualLocationRequiresReview = false;

        var warning = OrderPinAssignmentWarningPolicy.Create(order, geocodingResult: null);

        Assert.Null(warning);
    }

    [Fact]
    public void AssignmentWarningPolicy_StillWarnsWhenManualPinRequiresReview()
    {
        var order = CreateOrder("Via Principala", string.Empty, "7031", "Laax");
        order.Location = new GeoPoint(46.806, 9.258);
        order.IsLocationManuallySet = true;
        order.ManualLocationRequiresReview = true;

        var warning = OrderPinAssignmentWarningPolicy.Create(order, geocodingResult: null);

        Assert.NotNull(warning);
    }

    [Fact]
    public void AssignmentWarningPolicy_StillWarnsWhenManualFlagHasNoLocation()
    {
        var order = CreateOrder("Via Principala", string.Empty, "7031", "Laax");
        order.Location = null;
        order.IsLocationManuallySet = true;
        order.ManualLocationRequiresReview = false;

        var warning = OrderPinAssignmentWarningPolicy.Create(order, geocodingResult: null);

        Assert.NotNull(warning);
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
