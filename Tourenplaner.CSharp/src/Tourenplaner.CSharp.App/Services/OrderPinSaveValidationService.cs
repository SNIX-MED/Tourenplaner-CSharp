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
        OrderPinValidationPolicy.HasDeliveryAddressChanged(existing, updated);

    public static async Task<OrderPinSaveValidationResult> ValidateAsync(
        Order existing,
        Order updated,
        string tomTomApiKey,
        string? geocodeCachePath,
        Window? owner = null)
    {
        if (!OrderPinValidationPolicy.RequiresGeocoding(existing, updated))
        {
            return new OrderPinSaveValidationResult(true, null);
        }

        var resolution = await AddressGeocodingService.TryResolveOrderWithDiagnosticsAsync(
            updated,
            tomTomApiKey,
            geocodeCachePath);
        var geocodingResult = resolution.Result;
        var decision = OrderPinValidationPolicy.ResolveDecision(existing, updated, geocodingResult);

        if (decision == OrderPinValidationDecision.AcceptPreciseGeocoding)
        {
            OrderPinValidationPolicy.ApplyPreciseGeocoding(updated, geocodingResult!);
            return new OrderPinSaveValidationResult(true, geocodingResult);
        }

        var confirmation = OrderPinValidationPolicy.CreateManualConfirmation(
            existing,
            updated,
            geocodingResult);
        var dialog = new PinAddressInfoDialogWindow(
            updated,
            confirmation.AddressLine,
            geocodingResult,
            OrderPinValidationPolicy.ResolveFailureSummary(resolution.FailureReason),
            tomTomApiKey,
            confirmation.InitialLocation,
            initialLocationIsManual: true,
            initialLocationRequiresReview: confirmation.ExistingLocationIsManual,
            requireManualPlacement: confirmation.RequireManualPlacement)
        {
            Owner = owner ?? System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.SelectedLocation is null)
        {
            return new OrderPinSaveValidationResult(false, geocodingResult);
        }

        OrderPinValidationPolicy.ApplyManualConfirmation(
            updated,
            dialog.SelectedLocation,
            dialog.IsSelectedLocationManual,
            confirmation.AddressLine);
        return new OrderPinSaveValidationResult(true, geocodingResult);
    }

    internal static string BuildAddressLine(Order order) =>
        OrderPinValidationPolicy.BuildAddressLine(order, trimComponents: false);
}
