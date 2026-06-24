using System.Windows;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class OrderPinAssignmentWarningService
{
    public static void ShowIfNeeded(Order order, AddressGeocodingResult? geocodingResult)
    {
        if (order.Type != OrderType.Map)
        {
            return;
        }

        var addressLine = BuildAddressLine(order);
        if (string.IsNullOrWhiteSpace(addressLine))
        {
            addressLine = (order.Address ?? string.Empty).Trim();
        }

        if (geocodingResult is null)
        {
            AppMessageBox.Show(
                $"Der Auftrag {order.Id} wurde gespeichert, aber der Pin konnte keiner Adresse zugeordnet werden.{Environment.NewLine}{Environment.NewLine}" +
                $"Adresse: {addressLine}{Environment.NewLine}{Environment.NewLine}" +
                "Bitte prüfen Sie die Lieferadresse und setzen Sie den Pin danach erneut.",
                "Pin prüfen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (geocodingResult.IsPrecise)
        {
            return;
        }

        var matchDetails = string.IsNullOrWhiteSpace(geocodingResult.EntityType)
            ? geocodingResult.MatchType
            : $"{geocodingResult.MatchType} / {geocodingResult.EntityType}";

        AppMessageBox.Show(
            $"Der Auftrag {order.Id} wurde gespeichert, aber der Pin konnte nur ungefähr zugeordnet werden.{Environment.NewLine}{Environment.NewLine}" +
            $"Adresse: {addressLine}{Environment.NewLine}" +
            $"Trefferart: {matchDetails}{Environment.NewLine}{Environment.NewLine}" +
            "Bitte prüfen Sie die Position auf der Karte.",
            "Pin prüfen",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string BuildAddressLine(Order order)
    {
        var street = string.Join(" ", new[]
        {
            (order.DeliveryAddress?.Street ?? string.Empty).Trim(),
            (order.DeliveryAddress?.HouseNumber ?? string.Empty).Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var postalCity = string.Join(" ", new[]
        {
            (order.DeliveryAddress?.PostalCode ?? string.Empty).Trim(),
            (order.DeliveryAddress?.City ?? string.Empty).Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.Join(", ", new[] { street, postalCity }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}
