using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourPdfHtmlBuilderTests
{
    [Theory]
    [InlineData("Pascal Zander")]
    [InlineData("Pascal Zander, Anna <Muster>")]
    [InlineData(null)]
    public void Build_ShowsEmployeesSeparatelyBelowTitle(string? employees)
    {
        var snapshot = new RouteExportSnapshot("Donnerstag 05.11.2026", "05.11.2026", "08:00",
            null, null, [], [], [], null, EmployeeNames: employees);
        var html = TourPdfHtmlBuilder.Build(snapshot, null);
        var heading = "<h1 class=\"title\">Donnerstag 05.11.2026</h1>";
        Assert.Contains(heading, html);
        if (employees is null)
        {
            Assert.DoesNotContain("<div class=\"subtitle\">", html);
        }
        else
        {
            var line = $"<div class=\"subtitle\">{System.Net.WebUtility.HtmlEncode(employees)}</div>";
            Assert.Contains(line, html);
            Assert.True(html.IndexOf(line, StringComparison.Ordinal) > html.IndexOf(heading, StringComparison.Ordinal));
            Assert.DoesNotContain($"({employees})", html);
        }
    }

    [Fact]
    public void Build_ShowsTourNameAsHeadingWithoutRepeatingItInMetadata()
    {
        var snapshot = new RouteExportSnapshot("Mittwoch 11.11.2026 (Pascal Zander)", "11.11.2026", "07:30",
            "Renault", "Anhänger", [], [], [], null);
        var html = TourPdfHtmlBuilder.Build(snapshot, null);
        Assert.Contains("<h1 class=\"title\">Mittwoch 11.11.2026 (Pascal Zander)</h1>", html);
        Assert.DoesNotContain("<div class=\"meta-label\">Tour</div>", html);
        Assert.DoesNotContain("<div class=\"meta-value\">Mittwoch 11.11.2026 (Pascal Zander)</div>", html);
        Assert.Contains("<div class=\"meta-label\">Datum</div>", html);
        Assert.Contains("<div class=\"meta-label\">Start</div>", html);
        Assert.Contains("<div class=\"meta-label\">Stopps</div>", html);
        var dateIndex = html.IndexOf("<div class=\"meta-label\">Datum</div>", StringComparison.Ordinal);
        var startIndex = html.IndexOf("<div class=\"meta-label\">Start</div>", StringComparison.Ordinal);
        var stopsIndex = html.IndexOf("<div class=\"meta-label\">Stopps</div>", StringComparison.Ordinal);
        var vehicleIndex = html.IndexOf("<div class=\"meta-label\">Fahrzeug:</div>", StringComparison.Ordinal);
        Assert.True(dateIndex < startIndex && startIndex < stopsIndex && stopsIndex < vehicleIndex);
        Assert.Contains("<div class=\"meta-label\"></div><div class=\"meta-value\"></div>" + Environment.NewLine
            + "<div class=\"meta-label\">Stopps</div>", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Anhänger 1")]
    public void Build_UsesSeparateVehicleAndTrailerColumns(string? trailer)
    {
        var snapshot = new RouteExportSnapshot("Tour", "05.11.2026", "08:00", "Renault", trailer,
            [], [], [], null);
        var html = TourPdfHtmlBuilder.Build(snapshot, null);
        Assert.Contains("<div class=\"meta-label\">Fahrzeug:</div><div class=\"meta-value\">Renault</div>", html);
        Assert.DoesNotContain("Fahrzeug &amp; Anhänger", html);
        Assert.DoesNotContain("meta-value-wide", html);
        Assert.Equal(trailer is not null, System.Net.WebUtility.HtmlDecode(html).Contains("<div class=\"meta-label\">Anhänger:</div>"));
    }

    [Fact]
    public void Build_OmitsExportSubtitleAndEtaDetailsButPreservesDeliveryWindow()
    {
        var stops = Enumerable.Range(1, 3).Select(i => new RouteExportStopInfo(
            i, "A", "Kunde", "Musterstrasse 1", "Frei Bordsteinkante", "221855",
            47, 8, "08:30 - 09:30", "08:41", "08:20 - 09:40", "53.7 kg", "", 0)).ToList();
        var snapshot = new RouteExportSnapshot("Donnerstag 05.11.2026", "05.11.2026", "08:00",
            "Renault", null, stops, [], [], null);
        var html = TourPdfHtmlBuilder.Build(snapshot, null);
        Assert.DoesNotContain("Tour-Export für die aktuell geladene Route", html);
        Assert.DoesNotContain("Zeitfenster:", html);
        Assert.DoesNotContain("ETA", html);
        Assert.DoesNotContain("08:41", html);
        Assert.DoesNotContain("08:20 - 09:40", html);
        Assert.Equal(3, CountOccurrences(html, "<div class=\"stop-name\">Kunde (221855)</div>"));
        Assert.Equal(3, CountOccurrences(html, "08:30 - 09:30 | Gewicht: 53.7 kg"));
        Assert.DoesNotContain("Auftrag:", html);
        Assert.Contains("Donnerstag 05.11.2026", html);
        Assert.Contains("Renault", html);
    }

    [Theory]
    [InlineData("manual:bd7208809cfd4b7ebecb91992a89305c", false)]
    [InlineData("MANUAL:bd7208809cfd4b7ebecb91992a89305c", false)]
    [InlineData("1001", true)]
    public void Build_HidesInternalManualStopIds(string orderNumber, bool visible)
    {
        var stop = new RouteExportStopInfo(1, "A", "Test", "Leuzigenstrasse 23, 2540 Grenchen",
            "Gawela", orderNumber, 47, 8, "", "", "", "", "", 0);
        var snapshot = new RouteExportSnapshot("Tour", "28.10.2026", "07:30", null, null,
            [stop], [], [], null);
        var html = TourPdfHtmlBuilder.Build(snapshot, null);
        Assert.Equal(visible, html.Contains(orderNumber));
        Assert.Contains(visible ? "<div class=\"stop-name\">Test (1001)</div>" : "<div class=\"stop-name\">Test</div>", html);
        Assert.Contains("Leuzigenstrasse 23", html);
        Assert.Contains("Test", html);
    }

    [Fact]
    public void Build_RendersNotesAtCorrespondingStopAndEncodesHtml()
    {
        var snapshot = new RouteExportSnapshot(
            "Tour 1",
            "25.09.2026",
            "07:30",
            null,
            null,
            [
                new RouteExportStopInfo(
                    1,
                    "A",
                    "Kunde A",
                    "Musterstrasse 1",
                    "Gawela",
                    "1001",
                    47.0,
                    8.0,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0,
                    "Vorsicht <Glas> & zerbrechlich"),
                new RouteExportStopInfo(
                    2,
                    "B",
                    "Kunde B",
                    "Musterstrasse 2",
                    "Gawela",
                    "1002",
                    47.1,
                    8.1,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0)
            ],
            [],
            [],
            null);

        var html = TourPdfHtmlBuilder.Build(snapshot, null);

        Assert.Contains("Vorsicht &lt;Glas&gt; &amp; zerbrechlich", html);
        Assert.Equal(1, CountOccurrences(html, "class=\"stop-notes\""));
    }

    [Fact]
    public void Build_RendersSelectedAdditionalMaterialAndEncodesNames()
    {
        var snapshot = new RouteExportSnapshot(
            "Tour 1", "25.09.2026", "07:30", null, null, [], [], [], null,
            [new TourAdditionalMaterial
            {
                GroupName = "Montage & Spezial",
                WeightKg = 42.5,
                Items = [new AdditionalMaterialItem { Name = "Bohrer <gross>", WeightKg = 12.5 }]
            }]);

        var html = TourPdfHtmlBuilder.Build(snapshot, null);

        Assert.Contains("Zusätzliches Material", html);
        Assert.Contains($"Montage &amp; Spezial ({42.5:0.##} kg)", html);
        Assert.Contains($"Bohrer &lt;gross&gt; ({12.5:0.##} kg)", html);
    }

    private static int CountOccurrences(string value, string search)
    {
        return value.Split(search, StringSplitOptions.None).Length - 1;
    }
}
