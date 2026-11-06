using System.Reflection;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed partial class KarteSearchDetailsTests
{
    private static bool AddManualStop(KarteSectionViewModel vm, RouteStopItem stop) =>
        (bool)typeof(KarteSectionViewModel).GetMethod("AddManualStopToRoute",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                [stop, vm.ResolveTourIdForDetailsEditing(), vm.RouteStops.ToArray()])!;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ManualStopLookupCannotInsertIntoChangedTourOrDraft(bool changeTour)
    {
        var vm = CreateViewModel();
        var targetStops = vm.RouteStops.ToArray();
        if (changeTour)
            vm.SelectedTourOverviewItem = new SavedTourOverviewItem(42, "Andere Tour",
                "06.11.2026", "08:00", 1, "", "", "", "", "", 0, 0);
        else
            vm.RouteStops.Add(new RouteStopItem { OrderId = "A-100" });
        var changedStops = vm.RouteStops.ToArray();

        var added = (bool)typeof(KarteSectionViewModel).GetMethod("AddManualStopToRoute",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [NewManualStop(), 0, targetStops])!;

        Assert.False(added);
        Assert.Equal(changedStops, vm.RouteStops.ToArray());
        Assert.Equal(changeTour ? 42 : 0, vm.ResolveTourIdForDetailsEditing());
        Assert.Contains("erneut hinzufügen", vm.StatusText);
    }

    [Theory]
    [InlineData(double.NaN, 8)]
    [InlineData(47, double.PositiveInfinity)]
    [InlineData(-91, 8)]
    [InlineData(47, 181)]
    public async Task InvalidManualCoordinatesLeaveOverviewSelectionUntouched(double latitude, double longitude)
    {
        var vm = CreateViewModel();
        var selection = new SavedTourOverviewItem(42, "Bestehende Tour",
            "06.11.2026", "08:00", 1, "", "", "", "", "", 0, 0);
        vm.SelectedTourOverviewItem = selection;
        var stops = vm.RouteStops.ToArray();

        await vm.AddManualRouteStopAsync(latitude, longitude);

        Assert.Same(selection, vm.SelectedTourOverviewItem);
        Assert.Equal(stops, vm.RouteStops.ToArray());
        Assert.False(vm.SaveCurrentTourCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingManualStopCannotChangeTourAfterEditingOrLeaseEnds(bool leaseEnded)
    {
        var vm = CreateViewModel();
        SetPrivateField(vm, "_activeTourId", 42);
        SetPrivateField(vm, "_loadedTourLeaseId", 42);
        SetPrivateField(vm, "_isLoadedTourEditing", true);
        var targetStops = vm.RouteStops.ToArray();
        if (leaseEnded) SetPrivateField(vm, "_loadedTourLeaseId", 0);
        else SetPrivateField(vm, "_isLoadedTourEditing", false);

        var added = (bool)typeof(KarteSectionViewModel).GetMethod("AddManualStopToRoute",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [NewManualStop(), 42, targetStops])!;

        Assert.False(added);
        Assert.Equal(targetStops, vm.RouteStops.ToArray());
        Assert.Equal(42, vm.LoadedTourId);
        Assert.True(vm.IsLoadedTourReadOnly);
    }

    [Fact]
    public void PendingManualStopRejectsReloadedStopsEvenWhenTourIdIsUnchanged()
    {
        var vm = CreateViewModel();
        Assert.True(AddManualStop(vm, NewManualStop()));
        var targetStops = vm.RouteStops.ToArray();
        var index = vm.RouteStops.IndexOf(vm.SelectedRouteStop!);
        var replacement = NewManualStop();
        vm.RouteStops[index] = replacement;

        var added = (bool)typeof(KarteSectionViewModel).GetMethod("AddManualStopToRoute",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [NewManualStop(), 0, targetStops])!;

        Assert.False(added);
        Assert.Same(replacement, vm.RouteStops[index]);
        Assert.Equal(targetStops.Length, vm.RouteStops.Count);
    }

    private static RouteStopItem NewManualStop() => new()
    {
        OrderId = "manual:test", IsManualStop = true, Customer = "Material abholen",
        Address = "Teststrasse 1", Latitude = 47.2, Longitude = 8.2,
        PlannedStayMinutes = 25, EmployeeInfoText = "Beim Empfang melden",
        ManualAvisoStatus = "Keine Avisierung nötig"
    };

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public void FirstManualStopStartsSaveableDraftWithoutOverwritingOverviewSelection(int overviewId)
    {
        var vm = CreateViewModel();
        if (overviewId > 0)
            vm.SelectedTourOverviewItem = new SavedTourOverviewItem(overviewId, "Bestehende Tour",
                "06.11.2026", "08:00", 1, "", "", "", "", "", 0, 0);
        var stop = NewManualStop();

        Assert.True(AddManualStop(vm, stop));

        Assert.Equal(0, vm.LoadedTourId);
        Assert.Equal(0, vm.ResolveTourIdForDetailsEditing());
        Assert.True(vm.ShowRouteStopsPanel);
        Assert.Same(stop, vm.SelectedRouteStop);
        Assert.Equal(3, vm.RouteStops.Count);
        Assert.True(vm.RouteStops[0].IsCompanyAnchor);
        Assert.Same(stop, vm.RouteStops[1]);
        Assert.True(vm.RouteStops[2].IsCompanyAnchor);
        Assert.True(vm.SaveCurrentTourCommand.CanExecute(null));
        Assert.True(vm.SaveRouteAsTourCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualStopAppendsToExistingDraftWithoutResettingItsData(bool manualDraft)
    {
        var vm = CreateViewModel();
        var existing = manualDraft ? NewManualStop() : new RouteStopItem
            { OrderId = "A-100", Customer = "Kunde", PlannedStayMinutes = 37 };
        vm.RouteStops.Insert(vm.RouteStops.Count - 1, existing);
        vm.RouteName = "Eigener Entwurf";
        vm.RouteDate = "12.11.2026";
        vm.RouteStartHour = "10";
        vm.RouteStartMinute = "15";
        var added = NewManualStop();
        added.OrderId = "manual:second";

        Assert.True(AddManualStop(vm, added));

        Assert.Contains(existing, vm.RouteStops);
        Assert.True(vm.RouteStops.IndexOf(existing) < vm.RouteStops.IndexOf(added));
        Assert.Equal("Eigener Entwurf", vm.RouteName);
        Assert.Equal("12.11.2026", vm.RouteDate);
        Assert.Equal("10", vm.RouteStartHour);
        Assert.Equal("15", vm.RouteStartMinute);
        Assert.True(vm.SaveCurrentTourCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddingManualStopPreservesExistingStopsAndLoadedTourProtection(bool editing)
    {
        var vm = CreateViewModel();
        var order = new RouteStopItem { OrderId = "A-100", PlannedStayMinutes = 37 };
        vm.RouteStops.Add(order);
        vm.RouteName = "Eigener Tourname";
        SetPrivateField(vm, "_activeTourId", 42);
        SetPrivateField(vm, "_loadedTourLeaseId", editing ? 42 : 0);
        SetPrivateField(vm, "_isLoadedTourEditing", editing);
        var originalStops = vm.RouteStops.ToArray();
        var manual = NewManualStop();

        Assert.Equal(editing, AddManualStop(vm, manual));

        Assert.Equal(42, vm.LoadedTourId);
        Assert.Equal("Eigener Tourname", vm.RouteName);
        Assert.Contains(order, vm.RouteStops);
        Assert.Equal(37, order.PlannedStayMinutes);
        if (editing) Assert.Contains(manual, vm.RouteStops);
        else Assert.Equal(originalStops, vm.RouteStops.ToArray());
    }

    [Fact]
    public void CompanyAndPauseStopsAloneCannotBeSavedAsNewTour()
    {
        var vm = CreateViewModel();
        vm.RouteStops.Add(new RouteStopItem { OrderId = "pause:test", IsPauseStop = true });
        SetPrivateField(vm, "_hasUnsavedRouteChanges", true);

        Assert.False(vm.SaveCurrentTourCommand.CanExecute(null));
        Assert.False(vm.SaveRouteAsTourCommand.CanExecute(null));
    }

    [Fact]
    public async Task ManualOnlyDraftSurvivesPersistenceAndPassesWebfleetValidation()
    {
        var vm = CreateViewModel();
        Assert.True(AddManualStop(vm, NewManualStop()));
        var stops = (IReadOnlyList<MapRouteStop>)typeof(KarteSectionViewModel)
            .GetMethod("ToMapRouteStops", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null)!;
        var service = new MapRouteService();
        Assert.Empty(service.ExtractRouteOrderIds(stops));
        var tour = service.BuildTour(stops, 1, "Abholung", "06.11.2026", "08:00");
        var path = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"), "tours.json");
        var repository = new JsonToursRepository(path);
        await repository.SaveAsync([tour]);
        var loaded = Assert.Single(await repository.LoadAsync());
        var manual = Assert.Single(loaded.Stops, stop => stop.StopKind == "manual");

        Assert.Equal("manual:test", manual.Id);
        Assert.Equal(string.Empty, manual.Auftragsnummer);
        Assert.Equal("Material abholen", manual.Name);
        Assert.Equal(25, manual.ServiceMinutes);
        Assert.Equal("Beim Empfang melden", manual.EmployeeInfoText);
        Assert.Equal(47.2, manual.Lat);
        Assert.Equal(8.2, manual.Lon ?? manual.Lng);
        Assert.Empty(new WebfleetTourDispatchService().Validate(loaded,
            new Vehicle { WebfleetObjectUid = "test-vehicle" }));
    }
}
