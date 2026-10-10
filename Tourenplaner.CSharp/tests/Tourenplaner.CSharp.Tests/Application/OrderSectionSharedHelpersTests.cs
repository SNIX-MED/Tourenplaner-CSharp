using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class OrderSectionSharedHelpersTests
{
    [Theory]
    [InlineData("10015 10042 10099", "10015", true)]
    [InlineData("10015 10042 10099", "10042", true)]
    [InlineData("10015 10042 10099", "10077", false)]
    [InlineData("100", "10042", true)]
    public void MatchesSearchQuery_MatchesAnySpaceSeparatedOrderNumber(
        string query,
        string orderNumber,
        bool expected)
    {
        var order = new Order { Id = orderNumber };

        Assert.Equal(expected, OrderSectionSharedHelpers.MatchesSearchQuery(order, query));
    }

    [Fact]
    public void MatchesSearchQuery_PreservesPhraseSearchForOtherFields()
    {
        var order = new Order
        {
            Id = "10042",
            CustomerName = "Muster Firma AG",
            Address = "Hauptstrasse 10 Zürich"
        };

        Assert.True(OrderSectionSharedHelpers.MatchesSearchQuery(order, "Muster Firma"));
        Assert.True(OrderSectionSharedHelpers.MatchesSearchQuery(order, "10 Zürich"));
    }

    [Fact]
    public void MatchesSearchQuery_SearchesEveryDisplayedOrderColumn()
    {
        var order = new Order
        {
            Id = "221472",
            ScheduledDate = new DateOnly(2026, 4, 24),
            CustomerName = "J. Grimm AG",
            Address = "Fallback Adresse",
            OrderAddress = new OrderAddressInfo
            {
                Name = "J. Grimm AG",
                Street = "Auftragstrasse",
                HouseNumber = "7",
                PostalCode = "8000",
                City = "Zürich"
            },
            DeliveryAddress = new DeliveryAddressInfo
            {
                Name = "J. Grimm Lieferung",
                ContactPerson = "Michaela Ewald",
                Street = "Holzhusen",
                HouseNumber = "16",
                PostalCode = "8618",
                City = "Oetwil am See"
            },
            Phone = "0764416114",
            Notes = "Zufahrt über den Hintereingang",
            DeliveryType = DeliveryMethodExtensions.MitVerteilungMontage,
            OrderStatus = Order.PartiallyInTransitStatus,
            AssignedTourId = "Tour-42"
        };

        var queries = new[]
        {
            "221472",
            "24.04.2026",
            "2026-04-24",
            "J. Grimm AG",
            "Auftragstrasse 7, 8000 Zürich",
            "Holzhusen 16, 8618 Oetwil am See",
            "Michaela Ewald",
            "0764416114",
            "Hintereingang",
            "Mit Verteilung & Montage",
            "Teilweise Unterwegs",
            "Tour-42"
        };

        Assert.All(queries, query =>
            Assert.True(OrderSectionSharedHelpers.MatchesSearchQuery(order, query), query));
    }
}
