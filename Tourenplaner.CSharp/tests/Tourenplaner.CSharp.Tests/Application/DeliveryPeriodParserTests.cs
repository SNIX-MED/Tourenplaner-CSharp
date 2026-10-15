using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public class DeliveryPeriodParserTests
{
    private static readonly DateOnly ReferenceDate = new(2026, 10, 15);

    [Theory]
    [InlineData("Ab 31.10.2026", "2026-10-31", null)]
    [InlineData("ab 31.10", "2026-10-31", null)]
    [InlineData("31/10/2026", "2026-10-31", "2026-10-31")]
    [InlineData("19-23.10.2026", "2026-10-19", "2026-10-23")]
    [InlineData("31.10 - 5.11.2026", "2026-10-31", "2026-11-05")]
    [InlineData("19/10/2026 bis 23/10/2026", "2026-10-19", "2026-10-23")]
    [InlineData("ab KW51", "2026-12-14", null)]
    [InlineData("KW3 bis Kw6", "2026-01-12", "2026-02-08")]
    [InlineData("KW 43/2026", "2026-10-19", "2026-10-25")]
    [InlineData("Kalenderwoche 43", "2026-10-19", "2026-10-25")]
    [InlineData("KW 43-44", "2026-10-19", "2026-11-01")]
    [InlineData("KW43/26 bis KW44/26", "2026-10-19", "2026-11-01")]
    [InlineData("KW 3 bis KW 6 2027", "2027-01-18", "2027-02-14")]
    [InlineData("KW51 bis KW3", "2026-12-14", "2027-01-24")]
    [InlineData("KW51/2026 bis KW3", "2026-12-14", "2027-01-24")]
    [InlineData("KW51 bis KW3/2027", "2026-12-14", "2027-01-24")]
    [InlineData("KW 53/2026", "2026-12-28", "2027-01-03")]
    [InlineData("bis KW 51", null, "2026-12-20")]
    [InlineData("31.10.2026", "2026-10-31", "2026-10-31")]
    [InlineData("31.10.26", "2026-10-31", "2026-10-31")]
    [InlineData("2026-10-31", "2026-10-31", "2026-10-31")]
    [InlineData("19.–23.10.2026", "2026-10-19", "2026-10-23")]
    [InlineData("19.10. bis 23.10.2026", "2026-10-19", "2026-10-23")]
    [InlineData("von 19.10.2026 bis 23.10.2026", "2026-10-19", "2026-10-23")]
    [InlineData("zwischen 19.10.2026 und 23.10.2026", "2026-10-19", "2026-10-23")]
    [InlineData("2026-10-19 - 2026-10-23", "2026-10-19", "2026-10-23")]
    [InlineData("ab 31.10.", "2026-10-31", null)]
    [InlineData("bis 31.10.2026", null, "2026-10-31")]
    [InlineData("Dezember bis Januar", "2026-12-01", "2027-01-31")]
    [InlineData("20.12. bis 10.01.2027", "2026-12-20", "2027-01-10")]
    [InlineData("20.12.2026 bis 10.01.", "2026-12-20", "2027-01-10")]
    [InlineData("Oktober 2026", "2026-10-01", "2026-10-31")]
    [InlineData("Anfang Oktober", "2026-10-01", "2026-10-10")]
    [InlineData("Mitte Oktober 2026", "2026-10-11", "2026-10-20")]
    [InlineData("Ende Oktober 2026", "2026-10-21", "2026-10-31")]
    [InlineData("Anfang bis Mitte Oktober", "2026-10-01", "2026-10-20")]
    [InlineData("Anfang Oktober bis Mitte November", "2026-10-01", "2026-11-20")]
    [InlineData("ab Ende Oktober", "2026-10-21", null)]
    [InlineData("Ende Februar 2028", "2028-02-21", "2028-02-29")]
    [InlineData("31. Oktober 2026", "2026-10-31", "2026-10-31")]
    [InlineData("19. bis 23. Oktober 2026", "2026-10-19", "2026-10-23")]
    [InlineData("ca. KW 43", "2026-10-19", "2026-10-25")]
    [InlineData("Voraussichtlich Ende Oktober", "2026-10-21", "2026-10-31")]
    [InlineData("  AB   KW51  ", "2026-12-14", null)]
    [InlineData("März 2027", "2027-03-01", "2027-03-31")]
    public void Parse_RecognizesSupportedWindows(string text, string? start, string? end)
    {
        var result = DeliveryPeriodParser.Parse(text, ReferenceDate);
        Assert.NotNull(result);
        Assert.Equal(start is null ? null : DateOnly.Parse(start), result.Start);
        Assert.Equal(end is null ? null : DateOnly.Parse(end), result.End);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nach Absprache")]
    [InlineData("in drei Wochen")]
    [InlineData("KW 0")]
    [InlineData("KW 54")]
    [InlineData("KW 3/0000")]
    [InlineData("31.10.0000")]
    [InlineData("ab 31.10.10000")]
    [InlineData("KW52/9999 bis KW3")]
    [InlineData("KW 53/2025")]
    [InlineData("KW 4/2026 bis KW 3/2026")]
    [InlineData("31.02.2026")]
    [InlineData("29.02.2026")]
    [InlineData("01.13.2026")]
    [InlineData("23.10.2026 bis 19.10.2026")]
    [InlineData("10.12.2027 bis 10.01.2027")]
    [InlineData("19.")]
    [InlineData("19. bis 23.")]
    [InlineData("Auftrag 31.10.2026, Lieferdatum noch offen")]
    [InlineData("KW 3 oder KW 6")]
    [InlineData("nicht vor KW 3")]
    [InlineData("Anfang 31. Oktober")]
    public void Parse_DoesNotInventDatesForInvalidOrUnclearText(string? text)
    {
        Assert.Null(DeliveryPeriodParser.Parse(text, ReferenceDate));
    }

    [Fact]
    public void Parse_UsesIsoWeekYearAtNewYear()
    {
        var result = DeliveryPeriodParser.Parse("KW53", new DateOnly(2027, 1, 1));
        Assert.Equal(new DeliveryPeriod(new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 3)), result);
    }
}
