using System.Windows;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public sealed record OrderPinSaveValidationResult(
    bool Confirmed,
    AddressGeocodingResult? GeocodingResult);

public static class OrderPinSaveValidationService
{
    public static bool HasDeliveryAddressChanged(Order existing, Order updated) =>
        !string.Equals(BuildAddressKey(existing), BuildAddressKey(updated), StringComparison.Ordinal);

    public static async Task<OrderPinSaveValidationResult> ValidateAsync(
        Order existing,
        Order updated,
        string tomTomApiKey,
        string? geocodeCachePath,
        Window? owner = null)
    {
        if (!HasDeliveryAddressChanged(existing, updated) ||
            !DeliveryMethodExtensions.CanUseLiefertour(updated))
        {
            return new OrderPinSaveValidationResult(true, null);
        }

        var resolution = await AddressGeocodingService.TryResolveOrderWithDiagnosticsAsync(
            updated,
            tomTomApiKey,
            geocodeCachePath);
        var result = resolution.Result;

        if (!existing.IsLocationManuallySet && result?.IsPrecise == true)
        {
            updated.Location = result.Location;
            updated.IsLocationManuallySet = false;
            updated.ManualLocationAddress = string.Empty;
            updated.ManualLocationRequiresReview = false;
            return new OrderPinSaveValidationResult(true, result);
        }

        var address = BuildAddressLine(updated);
        var wasManual = existing.IsLocationManuallySet && existing.Location is not null;
        var initialLocation = wasManual
            ? existing.Location
            : result?.Location ?? existing.Location;
        var requireManualPlacement = !wasManual;
        var dialog = new PinAddressInfoDialogWindow(
            updated,
            address,
            result,
            ResolveFailureSummary(resolution.FailureReason),
            tomTomApiKey,
            initialLocation,
            initialLocationIsManual: true,
            initialLocationRequiresReview: wasManual,
            requireManualPlacement: requireManualPlacement)
        {
            Owner = owner ?? System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.SelectedLocation is null)
        {
            return new OrderPinSaveValidationResult(false, result);
        }

        updated.Location = dialog.SelectedLocation;
        updated.IsLocationManuallySet = dialog.IsSelectedLocationManual;
        updated.ManualLocationAddress = dialog.IsSelectedLocationManual ? address : string.Empty;
        updated.ManualLocationRequiresReview = false;
        return new OrderPinSaveValidationResult(true, result);
    }

    internal static string BuildAddressLine(Order order)
    {
        var delivery = order.DeliveryAddress;
        var street = string.Join(" ", new[] { delivery?.Street, delivery?.HouseNumber }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var city = string.Join(" ", new[] { delivery?.PostalCode, delivery?.City }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.Join(", ", new[] { street, city }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string BuildAddressKey(Order order)
    {
        var delivery = order.DeliveryAddress;
        return string.Join("|", new[]
        {
            Normalize(delivery?.Street),
            Normalize(delivery?.HouseNumber),
            Normalize(delivery?.PostalCode),
            Normalize(delivery?.City)
        });
    }

    private static string Normalize(string? value)
    {
        return SwissAddressNormalization.NormalizeForComparison(value)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string ResolveFailureSummary(AddressGeocodingFailureReason reason) => reason switch
    {
        AddressGeocodingFailureReason.MissingApiKey => "TomTom-API-Key fehlt. Der Pin muss manuell geprüft werden.",
        AddressGeocodingFailureReason.AuthenticationFailed => "TomTom-Zugang wurde abgelehnt. Der Pin muss manuell geprüft werden.",
        AddressGeocodingFailureReason.RateLimited => "TomTom drosselt die Adresssuche. Der Pin muss manuell geprüft werden.",
        AddressGeocodingFailureReason.Timeout => "Die TomTom-Anfrage hat zu lange gedauert. Der Pin muss manuell geprüft werden.",
        AddressGeocodingFailureReason.ConnectionFailed => "TomTom ist derzeit nicht erreichbar. Der Pin muss manuell geprüft werden.",
        _ => "Die neue Lieferadresse konnte nicht eindeutig zugeordnet werden. Bitte den Pin manuell setzen."
    };
}
