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
}
