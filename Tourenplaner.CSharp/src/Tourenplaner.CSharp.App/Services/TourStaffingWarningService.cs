using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class TourStaffingWarningService
{
    public const int RecommendedEmployeeCount = 2;
    public const string ConfirmationTitle = "Zweiten Mitarbeiter prüfen";
    public const string ConfirmationCancelText = "Zurück zur Auswahl";
    public const string ConfirmationAcceptText = "Trotzdem speichern";
    public const string DistributionRecommendationText =
        "Diese Tour enthält mindestens einen Auftrag mit der Lieferart ‚Mit Verteilung‘. Dafür werden zwei Mitarbeiter empfohlen.";

    public static TourStaffingAssessment Evaluate(
        IReadOnlyList<string>? employeeIds,
        IReadOnlyList<string>? deliveryTypes)
    {
        var employeeCount = (employeeIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var relevantDeliveryTypes = (deliveryTypes ?? [])
            .Where(IsDistributionDeliveryType)
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var containsPlainDistribution = relevantDeliveryTypes.Any(value =>
            string.Equals(value, DeliveryMethodExtensions.MitVerteilung, StringComparison.OrdinalIgnoreCase));
        var recommendation = containsPlainDistribution
            ? DistributionRecommendationText
            : $"Diese Tour enthält mindestens einen Auftrag mit der Lieferart ‚{DeliveryMethodExtensions.MitVerteilungMontage}‘. Dafür werden zwei Mitarbeiter empfohlen.";
        var selectionText = employeeCount switch
        {
            0 => "Kein Mitarbeiter ausgewählt.",
            1 => "Aktuell ist 1 Mitarbeiter ausgewählt.",
            _ => $"Aktuell sind {employeeCount} Mitarbeiter ausgewählt."
        };

        return new TourStaffingAssessment(
            employeeCount,
            relevantDeliveryTypes.Count > 0,
            relevantDeliveryTypes.Count > 0 && employeeCount < RecommendedEmployeeCount,
            relevantDeliveryTypes.Count > 0 && employeeCount == 1,
            relevantDeliveryTypes.Count > 0 ? $"{recommendation} {selectionText}" : string.Empty,
            relevantDeliveryTypes.Count > 0 && employeeCount == 1
                ? $"{recommendation} Aktuell ist nur ein Mitarbeiter zugeordnet. Möchten Sie die Tour trotzdem speichern?"
                : string.Empty);
    }

    public static bool TryBuildSingleEmployeeWarning(
        IReadOnlyList<string>? employeeIds,
        IReadOnlyList<string>? deliveryTypes,
        out string warning)
    {
        var assessment = Evaluate(employeeIds, deliveryTypes);
        warning = assessment.RequiresSaveConfirmation ? assessment.ConfirmationMessage : string.Empty;
        return assessment.RequiresSaveConfirmation;
    }

    public static bool IsDistributionDeliveryType(string? value) =>
        string.Equals(value?.Trim(), DeliveryMethodExtensions.MitVerteilung, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), DeliveryMethodExtensions.MitVerteilungMontage, StringComparison.OrdinalIgnoreCase);
}

public sealed record TourStaffingAssessment(
    int EmployeeCount,
    bool ContainsDistributionOrder,
    bool HasEmployeeRecommendationWarning,
    bool RequiresSaveConfirmation,
    string EmployeeRecommendationMessage,
    string ConfirmationMessage);
