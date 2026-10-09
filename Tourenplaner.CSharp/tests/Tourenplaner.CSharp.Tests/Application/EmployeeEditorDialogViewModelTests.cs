using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class EmployeeEditorDialogViewModelTests
{
    [Fact]
    public void DisabledWebfleet_DisablesAssignmentsAndPreservesExistingValues()
    {
        var seed = new EmployeeEditorSeed(
            Id: "employee-1",
            Name: "Test Mitarbeiter",
            ShortCode: "TM",
            Phone: string.Empty,
            HasProgramProfile: true,
            IsFavorite: false,
            RegisterAbsence: false,
            AbsenceStartDate: string.Empty,
            AbsenceEndDate: string.Empty,
            WebfleetObjectUid: "object-uid",
            WebfleetObjectNumber: "vehicle-1",
            WebfleetDriverUid: "driver-uid",
            WebfleetDriverNumber: "driver-1",
            WebfleetDriverName: "Test Fahrer");

        var viewModel = new EmployeeEditorDialogViewModel(
            seed,
            hasDuplicateWebfleetAssignment: true,
            isWebfleetEnabled: false);

        Assert.False(viewModel.IsWebfleetEnabled);
        Assert.False(viewModel.HasDuplicateWebfleetAssignment);
        Assert.Contains("deaktiviert", viewModel.NoWebfleetVehiclesMessage);
        Assert.True(viewModel.TryBuildResult(out var result, out _));
        Assert.Equal("object-uid", result.WebfleetObjectUid);
        Assert.Equal("driver-uid", result.WebfleetDriverUid);
    }
}
