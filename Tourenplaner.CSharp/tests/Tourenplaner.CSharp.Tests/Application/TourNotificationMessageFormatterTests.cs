using System.Globalization;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourNotificationMessageFormatterTests
{
    private static readonly CultureInfo SwissGerman = CultureInfo.GetCultureInfo("de-CH");

    [Fact]
    public void CreatedMessage_UsesTrimmedCustomNameInsteadOfInternalId()
    {
        var tour = new TourRecord { Id = 22, Name = "  QA-Testtour 18.10.2026  ", Date = "18.10.2026" };

        var message = TourNotificationMessageFormatter.FormatTourCreatedMessage(tour, SwissGerman);

        Assert.Equal("„QA-Testtour 18.10.2026“ wurde erstellt.", message);
        Assert.DoesNotContain("22", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatedMessage_WithoutName_UsesLocalizedDateFallback(string? name)
    {
        var tour = new TourRecord { Id = 22, Name = name!, Date = "18.10.2026" };

        Assert.Equal(
            "Tour vom 18.10.2026 wurde erstellt.",
            TourNotificationMessageFormatter.FormatTourCreatedMessage(tour, SwissGerman));
    }

    [Fact]
    public void CreatedMessage_NameThatLooksLikeGeneratedDefault_RemainsAValidName()
    {
        var tour = new TourRecord { Name = "Tour 18.10.2026", Date = "18.10.2026" };

        Assert.Equal(
            "„Tour 18.10.2026“ wurde erstellt.",
            TourNotificationMessageFormatter.FormatTourCreatedMessage(tour, SwissGerman));
    }

    [Fact]
    public void CreatedMessage_WithoutNameOrDate_UsesGenericFallback()
    {
        var tour = new TourRecord { Id = 22, Name = null!, Date = null! };

        Assert.Equal("Die neue Tour wurde erstellt.", TourNotificationMessageFormatter.FormatTourCreatedMessage(tour, SwissGerman));
    }

    [Fact]
    public void UpdatedMessage_UsesCurrentRenamedTour()
    {
        var tour = new TourRecord { Id = 22, Name = "Neue Bezeichnung", Date = "18.10.2026" };

        Assert.Equal("„Neue Bezeichnung“ wurde aktualisiert.", TourNotificationMessageFormatter.FormatTourUpdatedMessage(tour, SwissGerman));
    }

    [Theory]
    [InlineData("Üetliberg-Tour 🚚", "„Üetliberg-Tour 🚚“ wurde erstellt.")]
    [InlineData("Tour \"Nord\" {A}", "„Tour \"Nord\" {A}“ wurde erstellt.")]
    [InlineData("Tour\r\nNord", "„Tour Nord“ wurde erstellt.")]
    public void CreatedMessage_TreatsNamesAsPlainText(string name, string expected)
    {
        var tour = new TourRecord { Name = name, Date = "18.10.2026" };

        Assert.Equal(expected, TourNotificationMessageFormatter.FormatTourCreatedMessage(tour, SwissGerman));
    }

    [Fact]
    public void SequentialMessages_RemainBoundToTheirPersistedTour()
    {
        var first = new TourRecord { Id = 1, Name = "Erste Tour", Date = "18.10.2026" };
        var second = new TourRecord { Id = 2, Name = "Zweite Tour", Date = "19.10.2026" };

        var firstMessage = TourNotificationMessageFormatter.FormatTourCreatedMessage(first, SwissGerman);
        var secondMessage = TourNotificationMessageFormatter.FormatTourCreatedMessage(second, SwissGerman);

        Assert.Contains("Erste Tour", firstMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Zweite Tour", firstMessage, StringComparison.Ordinal);
        Assert.Contains("Zweite Tour", secondMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Erste Tour", secondMessage, StringComparison.Ordinal);
    }
}
