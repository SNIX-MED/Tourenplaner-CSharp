using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourPdfHtmlBuilderTests
{
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
