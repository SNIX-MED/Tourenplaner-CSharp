using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class TourStaffingWarningService
{
    public static bool TryBuildSingleEmployeeWarning(
        IReadOnlyList<string>? employeeIds,
        IReadOnlyList<string>? deliveryTypes,
        out string warning)
    {
        warning = string.Empty;

        var employeeCount = (employeeIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var relevantDeliveryTypes = (deliveryTypes ?? [])
            .Where(value =>
                string.Equals(value?.Trim(), DeliveryMethodExtensions.MitVerteilung, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value?.Trim(), DeliveryMethodExtensions.MitVerteilungMontage, StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => string.Equals(value, DeliveryMethodExtensions.MitVerteilung, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();

        if (employeeCount != 1 || relevantDeliveryTypes.Count == 0)
        {
            return false;
        }

        var deliveryTypeHint = relevantDeliveryTypes.Count == 1
            ? $"mind. 1 Auftrag mit der Lieferart \"{relevantDeliveryTypes[0]}\""
            : $"mind. 1 Auftrag mit den Lieferarten \"{relevantDeliveryTypes[0]}\" und/oder \"{relevantDeliveryTypes[1]}\"";

        warning =
            $"In dieser Tour ist {deliveryTypeHint} vorhanden.{Environment.NewLine}{Environment.NewLine}" +
            "Aktuell ist nur 1 Mitarbeiter zugeordnet." + Environment.NewLine + Environment.NewLine +
            "Soll die Tour trotzdem gespeichert werden?";
        return true;
    }
}
