using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class MapSearchResultValidatorTests
{
    [Fact]
    public void IsRelevant_AcceptsMatchingMunicipality()
    {
        var result = CreateResult(postalCode: "8134", municipality: "Adliswil", freeformAddress: "8134 Adliswil");

        Assert.True(MapSearchResultValidator.IsRelevant("Adliswil", result));
        Assert.True(MapSearchResultValidator.IsRelevant("8134 Adliswil, Schweiz", result));
    }

    [Fact]
    public void IsRelevant_AcceptsMatchingStreetAddressIgnoringUmlauts()
    {
        var result = CreateResult(
            postalCode: "8001",
            municipality: "Zürich",
            streetName: "Münstergasse",
            freeformAddress: "Münstergasse 12, 8001 Zürich");

        Assert.True(MapSearchResultValidator.IsRelevant("Munstergasse 12 Zürich", result));
    }

    [Theory]
    [InlineData("Hallenstadion")]
    [InlineData("Adliswl")]
    [InlineData("Talerwilen")]
    public void IsRelevant_RejectsUnrelatedFallbackResult(string query)
    {
        var result = CreateResult(postalCode: "6443", municipality: "Morschach", freeformAddress: "6443 Morschach");

        Assert.False(MapSearchResultValidator.IsRelevant(query, result));
    }

    private static AddressGeocodingResult CreateResult(
        string? postalCode = null,
        string? municipality = null,
        string? streetName = null,
        string? freeformAddress = null) =>
        new(
            new GeoPoint(46.0, 8.0),
            false,
            "query",
            "Geography",
            "Municipality",
            postalCode,
            municipality,
            streetName,
            freeformAddress,
            1);
}
