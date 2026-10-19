using Tourenplaner.CSharp.App.ViewModels.Sections;
using System.IO;
using System.Xml.Linq;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class RouteStopAvisoBadgeTests
{
    [Fact]
    public void ManualStopsKeepTheirColorAndNotifyWhenConverted()
    {
        var stop = new RouteStopItem { IsManualStop = true };
        Assert.Equal("#4EBA74", stop.AvisoBadgeColor);
        Assert.Equal("#4EBA74", new TourStopOverviewItem { IsManualStop = true }.AvisoBadgeColor);
        var changes = new List<string?>();
        stop.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        stop.IsManualStop = false;
        Assert.Contains(nameof(RouteStopItem.AvisoBadgeColor), changes);
        Assert.Equal("#64748B", stop.AvisoBadgeColor);
    }

    [Fact]
    public void MapBadgeBindingAllowsPauseColorTriggerToOverrideIt()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src"))) root = root.Parent;
        Assert.NotNull(root);
        var document = XDocument.Load(Path.Combine(root!.FullName, "src", "Tourenplaner.CSharp.App", "Views", "Sections", "KarteSectionView.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var badge = document.Descendants(ns + "Border").Single(border =>
            border.Elements(ns + "TextBlock").Any(text => (string?)text.Attribute("Text") == "{Binding DisplayPosition}"));
        Assert.Null(badge.Attribute("Background"));
        Assert.Contains(badge.Descendants(ns + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" && (string?)setter.Attribute("Value") == "{Binding AvisoBadgeColor}");
        Assert.Contains(badge.Descendants(ns + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsPauseStop}" &&
            trigger.Elements(ns + "Setter").Any(setter => (string?)setter.Attribute("Property") == "Background" && (string?)setter.Attribute("Value") == "#D99A21"));
    }

    [Theory]
    [InlineData("nicht avisiert", "#64748B")]
    [InlineData("informiert", "#F59E0B")]
    [InlineData("bestätigt", "#16A34A")]
    [InlineData(" BESTÄTIGT ", "#16A34A")]
    [InlineData("", "#64748B")]
    public void BothStopListsUseAvisoStatusColor(string status, string expected)
    {
        Assert.Equal(expected, new RouteStopItem { AvisoStatus = status }.AvisoBadgeColor);
        Assert.Equal(expected, new TourStopOverviewItem { AvisoStatus = status }.AvisoBadgeColor);
    }

    [Fact]
    public void ChangingMapStopStatusNotifiesBadgeBinding()
    {
        var stop = new RouteStopItem { AvisoStatus = "nicht avisiert" };
        var changes = new List<string?>();
        stop.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        stop.AvisoStatus = "bestätigt";
        Assert.Contains(nameof(RouteStopItem.AvisoBadgeColor), changes);
        Assert.Equal("#16A34A", stop.AvisoBadgeColor);
    }
}
