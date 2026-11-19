using System.Text.Json;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class CreateTourFullStatusTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DialogResultChangesOnlyFullMarker(bool full)
    {
        var vm = new CreateTourDialogViewModel(DateTime.Today.ToString("dd.MM.yyyy"), "Tour", "08", "00",
            [new("V", "Fahrzeug")], [], [new("E", "Mitarbeiter", false)],
            selectedVehicleId: "V", selectedEmployeeIds: ["E"]);
        vm.IsFull = !full;
        var saved = UnsavedChangesGuard.Capture(vm);
        vm.IsFull = full;
        Assert.NotEqual(saved, UnsavedChangesGuard.Capture(vm));
        Assert.True(vm.TryBuildResult(out var result, out var error), error);
        // The staffing confirmation uses a record copy; capacity details must survive it.
        result = result with { EmployeeRecommendationConfirmed = true };
        Assert.Equal(full, result.IsFull);

        var tour = new TourRecord { Id = 42, Name = "Bestehende Tour", VehicleId = "V", IsFull = !full,
            Stops = [new() { Id = "A", Auftragsnummer = "A" }], FullReason = "alt", FullNote = "alt" };
        var original = JsonSerializer.SerializeToNode(tour)!.AsObject();
        result.ApplyFullStatusTo(tour);
        Assert.Equal(full, tour.IsFull);
        var updated = JsonSerializer.SerializeToNode(tour)!.AsObject();
        foreach (var key in new[] { nameof(TourRecord.IsFull) })
        { original.Remove(key); updated.Remove(key); }
        Assert.Equal(original.ToJsonString(), updated.ToJsonString());
    }
}
