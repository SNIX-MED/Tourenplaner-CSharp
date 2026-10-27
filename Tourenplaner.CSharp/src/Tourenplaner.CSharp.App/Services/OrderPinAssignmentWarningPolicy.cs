using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

internal sealed record OrderPinAssignmentWarning(
    string Title,
    string Message);

internal static class OrderPinAssignmentWarningPolicy
{
    private const string WarningTitle = "Pin prüfen";

    public static OrderPinAssignmentWarning? Create(
        Order order,
        AddressGeocodingResult? geocodingResult)
    {
        if (!DeliveryMethodExtensions.CanUseLiefertour(order) || geocodingResult?.IsPrecise == true)
        {
            return null;
        }

        // A confirmed manual pin is intentionally not geocoded again during ordinary edits.
        // In that case a null result means "no lookup performed", not "address not found".
        if (order.IsLocationManuallySet &&
            order.Location is not null &&
            !order.ManualLocationRequiresReview)
        {
            return null;
        }

        var addressLine = OrderAddressFormatting.BuildAddressLine(
            order.DeliveryAddress?.Street,
            order.DeliveryAddress?.HouseNumber,
            order.DeliveryAddress?.PostalCode,
            order.DeliveryAddress?.City);
        if (string.IsNullOrWhiteSpace(addressLine))
        {
            addressLine = (order.Address ?? string.Empty).Trim();
        }

        return geocodingResult is null
            ? new OrderPinAssignmentWarning(
                WarningTitle,
                $"Der Auftrag {order.Id} wurde gespeichert, aber der Pin konnte keiner Adresse zugeordnet werden.{Environment.NewLine}{Environment.NewLine}" +
                $"Adresse: {addressLine}{Environment.NewLine}{Environment.NewLine}" +
                "Bitte prüfen Sie die Lieferadresse und setzen Sie den Pin danach erneut.")
            : new OrderPinAssignmentWarning(
                WarningTitle,
                $"Der Auftrag {order.Id} wurde gespeichert, aber der Pin konnte nur ungefähr zugeordnet werden.{Environment.NewLine}{Environment.NewLine}" +
                $"Adresse: {addressLine}{Environment.NewLine}" +
                $"Trefferart: {BuildMatchDetails(geocodingResult)}{Environment.NewLine}{Environment.NewLine}" +
                "Bitte prüfen Sie die Position auf der Karte.");
    }

    private static string BuildMatchDetails(AddressGeocodingResult geocodingResult) =>
        string.IsNullOrWhiteSpace(geocodingResult.EntityType)
            ? geocodingResult.MatchType
            : $"{geocodingResult.MatchType} / {geocodingResult.EntityType}";
}
