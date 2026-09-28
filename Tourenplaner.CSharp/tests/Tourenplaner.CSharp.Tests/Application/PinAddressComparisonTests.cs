using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class PinAddressComparisonTests
{
    [Theory]
    [InlineData("Grand Palais 1", "6440", "Brunnen", "Grand Parc", "Grand Parc 1, 6440 Ingenbohl", "Ingenbohl")]
    [InlineData("Seewenstrasse 130", "6423", "Seewen", "Seewernstrasse", "Seewernstrasse 130, 6423 Schwyz", "Schwyz")]
    public void Compare_ExplainsReportedImportWarnings(string street, string postal, string city,
        string foundStreet, string freeform, string municipality)
    {
        var result = Result(foundStreet, freeform, postal, municipality);
        var rows = PinAddressComparison.Compare(new DeliveryAddressInfo
            { Street = street, PostalCode = postal, City = city }, PinAddressComparison.GetFoundAddress(result));
        Assert.Equal("Abweichend", rows.Single(x => x.Field == "Strasse").Status);
        Assert.Equal("Gleich", rows.Single(x => x.Field == "Hausnummer").Status);
        Assert.Equal("Gleich", rows.Single(x => x.Field == "Postleitzahl").Status);
        Assert.Equal("Abweichend", rows.Single(x => x.Field == "Ort / Gemeinde").Status);
    }

    [Fact]
    public void ApplyFoundAddress_UpdatesDeliveryAndPinButPreservesRecipientAndInvoiceAddress()
    {
        var order = new Order
        {
            DeliveryAddress = new() { Name = "UFZ", ContactPerson = "Kontakt", Additional = "Etage -4" },
            OrderAddress = new() { Street = "Bahnhofstrasse", HouseNumber = "15" },
            Notes = "Lieferhinweis", ConcurrencyToken = "revision"
        };
        var result = Result("Seewernstrasse", "Seewernstrasse 130, 6423 Schwyz", "6423", "Schwyz");
        PinAddressComparison.ApplyFoundAddress(order, result);
        Assert.Equal("Seewernstrasse 130, 6423 Schwyz", order.Address);
        Assert.Equal("130", order.DeliveryAddress.HouseNumber);
        Assert.Equal(result.Location, order.Location);
        Assert.Equal("UFZ", order.DeliveryAddress.Name);
        Assert.Equal("Kontakt", order.DeliveryAddress.ContactPerson);
        Assert.Equal("Etage -4", order.DeliveryAddress.Additional);
        Assert.Equal("Bahnhofstrasse", order.OrderAddress.Street);
        Assert.Equal("Lieferhinweis", order.Notes);
        Assert.Equal("revision", order.ConcurrencyToken);
    }

    [Theory]
    [InlineData("Grand Parc, 6440 Ingenbohl")]
    [InlineData("6440 Ingenbohl")]
    public void IncompletePointAddress_CannotBeApplied(string freeform)
    {
        var result = Result("Grand Parc", freeform, "6440", "Ingenbohl");
        Assert.False(PinAddressComparison.CanUseFoundAddress(result));
        Assert.Throws<InvalidOperationException>(() => PinAddressComparison.ApplyFoundAddress(new Order(), result));
    }

    [Theory]
    [InlineData("10/12")]
    [InlineData("10-12")]
    [InlineData("130a")]
    public void FoundAddress_PreservesHouseNumberSuffixAndRange(string number)
    {
        var result = Result("Weg", $"Weg {number}, 6423 Schwyz", "6423", "Schwyz");
        Assert.Equal(number, PinAddressComparison.GetFoundAddress(result).HouseNumber);
    }

    [Fact]
    public void MissingResult_CannotBeSelected() => Assert.False(PinAddressComparison.CanUseFoundAddress(null));

    [Fact]
    public void GoogleMapsLink_EncodesAddressAsSingleQueryParameter()
    {
        var uri = new Uri(PinAddressComparison.GoogleMapsUrl("Weg & Platz 1, 8000 Zürich"));
        Assert.Equal("www.google.com", uri.Host);
        Assert.Contains("Weg%20%26%20Platz", uri.Query);
        Assert.EndsWith(", Schweiz", Uri.UnescapeDataString(uri.Query));
    }

    [Fact]
    public void TomTomLink_EncodesAddressAsSingleQueryParameter()
    {
        var uri = new Uri(PinAddressComparison.TomTomUrl("Weg & Platz 1, 8000 Zürich"));
        Assert.Equal("plan.tomtom.com", uri.Host);
        Assert.Equal("/de", uri.AbsolutePath);
        Assert.Equal("?q=Weg & Platz 1, 8000 Zürich, Schweiz", Uri.UnescapeDataString(uri.Query));
        Assert.Contains("%26", uri.Query);
    }

    private static AddressGeocodingResult Result(string street, string freeform, string postal, string city) =>
        new(new GeoPoint(47.0187873, 8.6270755), false, "Anfrage", "Point Address", null,
            postal, city, street, freeform, 1);
}
