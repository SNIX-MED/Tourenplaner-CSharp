using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourPdfHtmlBuilderTests
{
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

    private static int CountOccurrences(string value, string search)
    {
        return value.Split(search, StringSplitOptions.None).Length - 1;
    }
}
