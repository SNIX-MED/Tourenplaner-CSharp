using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class CreateTourStaffingRecommendationTests
{
    [Fact]
    public void DistributionOrder_ReactsImmediatelyToEmployeeSelection()
    {
        var model = CreateModel([DeliveryMethodExtensions.MitVerteilung]);

        Assert.True(model.HasEmployeeRecommendationWarning);
        Assert.Equal("Kein Mitarbeiter ausgewählt", model.SelectedEmployeesSummary);
        Assert.False(model.TryBuildResult(out _, out var noEmployeeError));
        Assert.Contains("mindestens 1 Mitarbeiter", noEmployeeError);

        model.Employees[0].IsSelected = true;
        Assert.True(model.HasEmployeeRecommendationWarning);
        Assert.True(model.RequiresEmployeeRecommendationConfirmation);
        Assert.Equal("1 Mitarbeiter ausgewählt", model.SelectedEmployeesSummary);
        Assert.True(model.TryBuildResult(out _, out var oneEmployeeError), oneEmployeeError);

        model.Employees[1].IsSelected = true;
        Assert.False(model.HasEmployeeRecommendationWarning);
        Assert.False(model.RequiresEmployeeRecommendationConfirmation);
        Assert.Equal("2 Mitarbeiter ausgewählt", model.SelectedEmployeesSummary);

        model.Employees[1].IsSelected = false;
        Assert.True(model.HasEmployeeRecommendationWarning);
        Assert.True(model.RequiresEmployeeRecommendationConfirmation);
    }

    [Fact]
    public void NonDistributionOrder_WithOneEmployee_ShowsNoRecommendation()
    {
        var model = CreateModel([DeliveryMethodExtensions.FreiBordsteinkante]);
        model.Employees[0].IsSelected = true;

        Assert.False(model.ContainsDistributionOrder);
        Assert.False(model.HasEmployeeRecommendationWarning);
        Assert.False(model.RequiresEmployeeRecommendationConfirmation);
    }

    [Fact]
    public void ChangingIncludedOrders_ReevaluatesRecommendationImmediately()
    {
        var model = CreateModel([]);
        model.Employees[0].IsSelected = true;
        Assert.False(model.HasEmployeeRecommendationWarning);

        model.UpdateSingleEmployeeWarningDeliveryTypes([DeliveryMethodExtensions.MitVerteilung]);
        Assert.True(model.HasEmployeeRecommendationWarning);

        model.UpdateSingleEmployeeWarningDeliveryTypes([]);
        Assert.False(model.HasEmployeeRecommendationWarning);
    }

    [Fact]
    public void ExistingDistributionTour_WithOneEmployee_OpensWithWarning()
    {
        var model = CreateModel([DeliveryMethodExtensions.MitVerteilung], ["employee-1"]);

        Assert.True(model.HasEmployeeRecommendationWarning);
        Assert.True(model.RequiresEmployeeRecommendationConfirmation);
        Assert.Contains("Aktuell ist 1 Mitarbeiter ausgewählt", model.EmployeeRecommendationMessage);
    }

    [Fact]
    public void SwitchingVehicleMode_DoesNotTurnRecommendationIntoValidationError()
    {
        var model = CreateModel([DeliveryMethodExtensions.MitVerteilung], ["employee-1"]);
        Assert.True(model.TryBuildResult(out _, out var error), error);

        model.UseSecondaryVehicle = true;
        Assert.True(model.HasEmployeeRecommendationWarning);
        Assert.False(model.TryBuildResult(out _, out var secondaryVehicleError));
        Assert.Contains("zweite Fahrzeug", secondaryVehicleError);
    }

    private static CreateTourDialogViewModel CreateModel(
        IReadOnlyList<string> deliveryTypes,
        IReadOnlyList<string>? selectedEmployees = null)
    {
        var model = new CreateTourDialogViewModel(
            DateTime.Today.ToString("dd.MM.yyyy"),
            string.Empty,
            "08",
            "00",
            [new TourLookupOption("vehicle-1", "Fahrzeug 1"), new TourLookupOption("vehicle-2", "Fahrzeug 2")],
            [],
            [new TourEmployeeOption("employee-1", "Mitarbeiter 1", false), new TourEmployeeOption("employee-2", "Mitarbeiter 2", false)],
            deliveryTypes,
            selectedVehicleId: "vehicle-1",
            selectedEmployeeIds: selectedEmployees);
        return model;
    }
}
