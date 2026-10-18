using System.Windows;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class OrderPinAssignmentWarningService
{
    public static void ShowIfNeeded(Order order, AddressGeocodingResult? geocodingResult)
    {
        var warning = OrderPinAssignmentWarningPolicy.Create(order, geocodingResult);
        if (warning is null)
        {
            return;
        }

        AppMessageBox.Show(
            warning.Message,
            warning.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
