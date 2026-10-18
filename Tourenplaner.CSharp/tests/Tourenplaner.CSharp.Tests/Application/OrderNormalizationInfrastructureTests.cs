using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderNormalizationInfrastructureTests
{
    [Fact]
    public void AddressFormatting_BuildsTrimmedStreetPostalAndCombinedLines()
    {
        Assert.Equal("Seewenstrasse 130", OrderAddressFormatting.BuildStreetLine(" Seewenstrasse ", " 130 "));
        Assert.Equal("6423 Seewen", OrderAddressFormatting.BuildPostalCityLine(" 6423 ", " Seewen "));
        Assert.Equal(
            "Seewenstrasse 130, 6423 Seewen",
            OrderAddressFormatting.BuildAddressLine(" Seewenstrasse ", " 130 ", " 6423 ", " Seewen "));
    }

    [Fact]
    public void AddressFormatting_CanPreserveComponentWhitespaceForLegacyPinDialogText()
    {
        Assert.Equal(
            " Seewenstrasse   130 ,  6423   Seewen ",
            OrderAddressFormatting.BuildAddressLine(
                " Seewenstrasse ",
                " 130 ",
                " 6423 ",
                " Seewen ",
                trimComponents: false));
    }

    [Fact]
    public void SwissPinProfile_NormalizesSwissSpellingDiacriticsCaseAndWhitespace()
    {
        var first = OrderAddressFormatting.BuildStableKey(
            " Straße ",
            " 12 A ",
            " 9000 ",
            " St. Gallen ",
            OrderAddressComparisonProfile.SwissPinValidation);
        var second = OrderAddressFormatting.BuildStableKey(
            "STRASSE",
            "12A",
            "9000",
            "Sankt Gállen",
            OrderAddressComparisonProfile.SwissPinValidation);

        Assert.Equal(first, second);
    }

    [Fact]
    public void XmlReimportProfile_TrimsButPreservesCaseDiacriticsAndInternalWhitespace()
    {
        Assert.Equal(
            "Séewen Strasse",
            OrderAddressFormatting.NormalizeForComparison(
                "  Séewen Strasse  ",
                OrderAddressComparisonProfile.XmlReimport));
        Assert.NotEqual(
            OrderAddressFormatting.NormalizeForComparison("Séewen", OrderAddressComparisonProfile.XmlReimport),
            OrderAddressFormatting.NormalizeForComparison("SEEWEN", OrderAddressComparisonProfile.XmlReimport));
    }

    [Theory]
    [InlineData(4, 2.5, 0, 2.5, 10)]
    [InlineData(4, 0, 10, 2.5, 10)]
    [InlineData(0, 0, 10, 10, 10)]
    [InlineData(-4, -2.5, -10, 2.5, -10)]
    public void ProductWeightNormalization_PreservesLegacyWeightResolution(
        int quantity,
        double unitWeightKg,
        double totalWeightKg,
        double expectedUnitWeightKg,
        double expectedTotalWeightKg)
    {
        var product = new OrderProductInfo
        {
            Quantity = quantity,
            UnitWeightKg = unitWeightKg,
            WeightKg = totalWeightKg
        };

        Assert.Equal(expectedUnitWeightKg, OrderProductWeightNormalization.ResolveUnitWeightKg(product));
        Assert.Equal(expectedTotalWeightKg, OrderProductWeightNormalization.ResolveTotalWeightKg(product));
    }
}
