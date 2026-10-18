using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

internal enum OrderPinValidationDecision
{
    AcceptWithoutGeocoding,
    AcceptPreciseGeocoding,
    RequestManualConfirmation
}

internal sealed record OrderPinManualConfirmation(
    string AddressLine,
    GeoPoint? InitialLocation,
    bool ExistingLocationIsManual,
    bool RequireManualPlacement);

internal static class OrderPinValidationPolicy
{
    public static bool HasDeliveryAddressChanged(Order existing, Order updated) =>
        !string.Equals(BuildAddressKey(existing), BuildAddressKey(updated), StringComparison.Ordinal);

    public static bool RequiresGeocoding(Order existing, Order updated) =>
        HasDeliveryAddressChanged(existing, updated) &&
        DeliveryMethodExtensions.CanUseLiefertour(updated);

    public static OrderPinValidationDecision ResolveDecision(
        Order existing,
        Order updated,
        AddressGeocodingResult? geocodingResult)
    {
        if (!RequiresGeocoding(existing, updated))
        {
            return OrderPinValidationDecision.AcceptWithoutGeocoding;
        }

        return !existing.IsLocationManuallySet && geocodingResult?.IsPrecise == true
            ? OrderPinValidationDecision.AcceptPreciseGeocoding
            : OrderPinValidationDecision.RequestManualConfirmation;
    }

    public static void ApplyPreciseGeocoding(Order updated, AddressGeocodingResult geocodingResult)
    {
        updated.Location = geocodingResult.Location;
        updated.IsLocationManuallySet = false;
        updated.ManualLocationAddress = string.Empty;
        updated.ManualLocationRequiresReview = false;
    }

    public static OrderPinManualConfirmation CreateManualConfirmation(
        Order existing,
        Order updated,
        AddressGeocodingResult? geocodingResult)
    {
        var existingLocationIsManual = existing.IsLocationManuallySet && existing.Location is not null;
        var initialLocation = existingLocationIsManual
            ? existing.Location
            : geocodingResult?.Location ?? existing.Location;
        return new OrderPinManualConfirmation(
            BuildAddressLine(updated, trimComponents: false),
            initialLocation,
            existingLocationIsManual,
            RequireManualPlacement: !existingLocationIsManual);
    }

    public static void ApplyManualConfirmation(
        Order updated,
        GeoPoint selectedLocation,
        bool selectedLocationIsManual,
        string addressLine)
    {
        updated.Location = selectedLocation;
        updated.IsLocationManuallySet = selectedLocationIsManual;
        updated.ManualLocationAddress = selectedLocationIsManual ? addressLine : string.Empty;
        updated.ManualLocationRequiresReview = false;
    }

    public static string BuildAddressLine(Order order, bool trimComponents = true) =>
        OrderAddressFormatting.BuildAddressLine(
            order.DeliveryAddress?.Street,
            order.DeliveryAddress?.HouseNumber,
            order.DeliveryAddress?.PostalCode,
            order.DeliveryAddress?.City,
            trimComponents);

    public static string ResolveFailureSummary(AddressGeocodingFailureReason reason) => reason switch
    {
        AddressGeocodingFailureReason.MissingApiKey => "TomTom-API-Key fehlt. Der Pin muss manuell geprüft werden.",
        AddressGeocodingFailureReason.AuthenticationFailed => "TomTom-Zugang wurde abgelehnt. Der Pin muss manuell geprüft werden.",
        AddressGeocodingFailureReason.RateLimited => "TomTom drosselt die Adresssuche. Der Pin muss manuell geprüft werden.",
        AddressGeocodingFailureReason.Timeout => "Die TomTom-Anfrage hat zu lange gedauert. Der Pin muss manuell geprüft werden.",
        AddressGeocodingFailureReason.ConnectionFailed => "TomTom ist derzeit nicht erreichbar. Der Pin muss manuell geprüft werden.",
        _ => "Die neue Lieferadresse konnte nicht eindeutig zugeordnet werden. Bitte den Pin manuell setzen."
    };

    private static string BuildAddressKey(Order order) =>
        OrderAddressFormatting.BuildStableKey(
            order.DeliveryAddress?.Street,
            order.DeliveryAddress?.HouseNumber,
            order.DeliveryAddress?.PostalCode,
            order.DeliveryAddress?.City,
            OrderAddressComparisonProfile.SwissPinValidation);
}
