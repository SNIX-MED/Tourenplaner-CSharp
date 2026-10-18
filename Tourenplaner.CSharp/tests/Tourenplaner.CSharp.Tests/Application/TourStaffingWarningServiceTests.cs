using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class TourStaffingWarningServiceTests
{
    [Theory]
    [InlineData(DeliveryMethodExtensions.MitVerteilung)]
    [InlineData(DeliveryMethodExtensions.MitVerteilungMontage)]
    public void TryBuildSingleEmployeeWarning_WarnsForRelevantOrderAndOneEmployee(string deliveryType)
    {
        var warned = TourStaffingWarningService.TryBuildSingleEmployeeWarning(
            ["employee-1"],
            [deliveryType],
            out var warning);

        Assert.True(warned);
        Assert.Contains(deliveryType, warning);
        Assert.Contains("nur ein Mitarbeiter", warning);
        Assert.Contains("Möchten Sie die Tour trotzdem speichern?", warning);
    }

    [Fact]
    public void TryBuildSingleEmployeeWarning_DoesNotWarnWithTwoEmployees()
    {
        var warned = TourStaffingWarningService.TryBuildSingleEmployeeWarning(
            ["employee-1", "employee-2"],
            [DeliveryMethodExtensions.MitVerteilungMontage],
            out _);

        Assert.False(warned);
    }

    [Fact]
    public void TryBuildSingleEmployeeWarning_DoesNotWarnForCurbsideDelivery()
    {
        var warned = TourStaffingWarningService.TryBuildSingleEmployeeWarning(
            ["employee-1"],
            [DeliveryMethodExtensions.FreiBordsteinkante],
            out _);

        Assert.False(warned);
    }

    [Fact]
    public void Evaluate_DistributionOrderWithoutEmployee_ShowsInlineWarningButNotConfirmation()
    {
        var assessment = TourStaffingWarningService.Evaluate([], [DeliveryMethodExtensions.MitVerteilung]);

        Assert.True(assessment.ContainsDistributionOrder);
        Assert.True(assessment.HasEmployeeRecommendationWarning);
        Assert.False(assessment.RequiresSaveConfirmation);
        Assert.Contains("Kein Mitarbeiter ausgewählt", assessment.EmployeeRecommendationMessage);
    }

    [Fact]
    public void Evaluate_DistributionOrderWithTwoEmployees_NeedsNoWarning()
    {
        var assessment = TourStaffingWarningService.Evaluate(
            ["employee-1", "employee-2"],
            [DeliveryMethodExtensions.MitVerteilung]);

        Assert.False(assessment.HasEmployeeRecommendationWarning);
        Assert.False(assessment.RequiresSaveConfirmation);
    }
}
