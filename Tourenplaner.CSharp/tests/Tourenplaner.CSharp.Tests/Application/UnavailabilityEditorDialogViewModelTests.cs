using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class UnavailabilityEditorDialogViewModelTests
{
    [Fact]
    public void TryBuildResult_SupportsMultiplePeriodsTimesAndNotes()
    {
        var viewModel = new UnavailabilityEditorDialogViewModel(
        [
            new ResourceUnavailabilityPeriod
            {
                StartDate = "2026-10-10", EndDate = "2026-10-10",
                StartTime = "08:30", EndTime = "12:00", Note = "Arzt"
            },
            new ResourceUnavailabilityPeriod
            {
                StartDate = "2026-10-20", EndDate = "2026-10-22", Note = "Ferien"
            }
        ]);

        Assert.True(viewModel.TryBuildResult(out var result, out var error), error);
        Assert.Equal(2, result.Count);
        Assert.Equal("08:30", result[0].StartTime);
        Assert.Equal("Arzt", result[0].Note);
        Assert.Equal("Ferien", result[1].Note);
    }

    [Fact]
    public void TryBuildResult_RejectsEndBeforeStart()
    {
        var viewModel = new UnavailabilityEditorDialogViewModel(
        [
            new ResourceUnavailabilityPeriod
            {
                StartDate = "2026-10-10", EndDate = "2026-10-10",
                StartTime = "14:00", EndTime = "09:00"
            }
        ]);

        Assert.False(viewModel.TryBuildResult(out _, out var error));
        Assert.Contains("Ende", error);
    }
}
