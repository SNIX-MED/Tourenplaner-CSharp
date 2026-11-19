using System.Windows;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public static class TourFullWarning
{
    public static string Description(TourRecord tour) => "Tour ist manuell als voll markiert.";

    public static bool Confirm(TourRecord? tour, Func<string, bool>? confirmation = null)
    {
        if (tour?.IsFull != true) return true;
        var message = $"{tour.Name} (Tour {tour.Id})\n\n{Description(tour)}\n\nTrotzdem weitere Aufträge hinzufügen?";
        return confirmation is not null ? confirmation(message) :
            AppMessageBox.Show(message, "Tour ist voll", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
