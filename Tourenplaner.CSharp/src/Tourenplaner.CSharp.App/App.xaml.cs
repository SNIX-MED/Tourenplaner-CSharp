using System.IO;
using System.Linq;
using System.Windows;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App;

public partial class App : System.Windows.Application
{
    private string _logPath = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tourenplaner.CSharp",
            "data");

        Directory.CreateDirectory(dataRoot);
        _logPath = Path.Combine(dataRoot, "app-crash.log");
        AttachGlobalExceptionLogging();
        var settingsPath = Path.Combine(dataRoot, "settings.json");

        var orderRepository = new JsonOrderRepository(Path.Combine(dataRoot, "orders.json"));
        var tourRepository = new JsonTourRepository(Path.Combine(dataRoot, "tours.json"));
        var employeeRepository = new JsonEmployeeRepository(Path.Combine(dataRoot, "employees.json"));
        var vehicleRepository = new JsonVehicleRepository(Path.Combine(dataRoot, "vehicles.json"));

        var snapshotService = new AppSnapshotService(orderRepository, tourRepository, employeeRepository, vehicleRepository);
        var ordersJsonPath = Path.Combine(dataRoot, "orders.json");
        var toursJsonPath = Path.Combine(dataRoot, "tours.json");
        var employeesJsonPath = Path.Combine(dataRoot, "employees.json");
        var vehiclesJsonPath = Path.Combine(dataRoot, "vehicles.json");
        var settingsJsonPath = settingsPath;
        _ = RunTourIntegrityCheckOnStartup(toursJsonPath, settingsJsonPath);

        var mainWindow = new MainWindow
        {
            DataContext = new MainShellViewModel(
                snapshotService,
                ordersJsonPath,
                toursJsonPath,
                employeesJsonPath,
                vehiclesJsonPath,
                settingsJsonPath,
                dataRoot)
        };

        mainWindow.Show();
    }

    private async Task<string?> RunTourIntegrityCheckOnStartup(string toursJsonPath, string settingsJsonPath)
    {
        try
        {
            var toursRepository = new JsonToursRepository(toursJsonPath);
            var settingsRepository = new JsonAppSettingsRepository(settingsJsonPath);
            var settings = await settingsRepository.LoadAsync();
            var tours = (await toursRepository.LoadAsync()).ToList();

            var companyName = string.IsNullOrWhiteSpace(settings.CompanyName) ? "Firma" : settings.CompanyName.Trim();
            var companyAddress = BuildCompanyAddress(settings);

            var touchedTours = 0;
            var insertedStart = 0;
            var insertedEnd = 0;
            var removedDuplicateCompanyStops = 0;
            var repairedRequiredFields = 0;
            var reindexedTours = 0;

            foreach (var tour in tours)
            {
                var changed = false;
                tour.Stops ??= new List<TourStopRecord>();

                var originalOrder = tour.Stops.Select(x => x.Order).ToList();
                var existingCompanyCount = tour.Stops.Count(TourStopIdentity.IsCompanyStop);
                var start = tour.Stops.FirstOrDefault(IsCompanyStartStop);
                var end = tour.Stops.LastOrDefault(IsCompanyEndStop);

                if (start is null)
                {
                    start = new TourStopRecord();
                    insertedStart++;
                    changed = true;
                }

                if (end is null)
                {
                    end = new TourStopRecord();
                    insertedEnd++;
                    changed = true;
                }

                start.Id = TourStopIdentity.CompanyStartStopId;
                start.Auftragsnummer = TourStopIdentity.CompanyStartOrderNumber;
                start.Name = $"{companyName} (Start)";
                start.Address = companyAddress;
                start.ServiceMinutes = 0;

                end.Id = TourStopIdentity.CompanyEndStopId;
                end.Auftragsnummer = TourStopIdentity.CompanyEndOrderNumber;
                end.Name = $"{companyName} (Ende)";
                end.Address = companyAddress;
                end.ServiceMinutes = 0;

                var middle = tour.Stops
                    .Where(x => !TourStopIdentity.IsCompanyStop(x))
                    .ToList();

                foreach (var stop in middle)
                {
                    var stopChanged = false;
                    if (string.IsNullOrWhiteSpace(stop.Id))
                    {
                        stop.Id = BuildFallbackStopId(stop);
                        stopChanged = true;
                    }

                    if (string.IsNullOrWhiteSpace(stop.Name))
                    {
                        stop.Name = !string.IsNullOrWhiteSpace(stop.Address)
                            ? stop.Address.Trim()
                            : (!string.IsNullOrWhiteSpace(stop.Auftragsnummer) ? $"Auftrag {stop.Auftragsnummer.Trim()}" : "Stopp");
                        stopChanged = true;
                    }

                    if (string.IsNullOrWhiteSpace(stop.Address))
                    {
                        stop.Address = "Adresse nicht gesetzt";
                        stopChanged = true;
                    }

                    if (stop.ServiceMinutes < 0)
                    {
                        stop.ServiceMinutes = 0;
                        stopChanged = true;
                    }

                    if (stopChanged)
                    {
                        repairedRequiredFields++;
                        changed = true;
                    }
                }

                if (existingCompanyCount > 2)
                {
                    removedDuplicateCompanyStops += existingCompanyCount - 2;
                    changed = true;
                }

                tour.Stops = [start, .. middle, end];
                for (var i = 0; i < tour.Stops.Count; i++)
                {
                    tour.Stops[i].Order = i + 1;
                }

                var reordered = originalOrder.Count != tour.Stops.Count ||
                                !tour.Stops.Select(x => x.Order).SequenceEqual(originalOrder);
                if (reordered)
                {
                    reindexedTours++;
                    changed = true;
                }

                if (changed)
                {
                    touchedTours++;
                }
            }

            if (touchedTours == 0)
            {
                return null;
            }

            await toursRepository.SaveAsync(tours);
            return
                $"Tour-Integritätscheck abgeschlossen.{Environment.NewLine}" +
                $"Reparierte Touren: {touchedTours}{Environment.NewLine}" +
                $"Start ergänzt: {insertedStart}{Environment.NewLine}" +
                $"Ziel ergänzt: {insertedEnd}{Environment.NewLine}" +
                $"Duplikate entfernt: {removedDuplicateCompanyStops}{Environment.NewLine}" +
                $"Pflichtfelder repariert: {repairedRequiredFields}{Environment.NewLine}" +
                $"Reihenfolge neu gesetzt: {reindexedTours}";
        }
        catch (Exception ex)
        {
            TryLogException("RunTourIntegrityCheckOnStartup", ex);
            return null;
        }
    }

    private static bool IsCompanyStartStop(TourStopRecord stop)
    {
        return string.Equals(stop.Id, TourStopIdentity.CompanyStartStopId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stop.Auftragsnummer, TourStopIdentity.CompanyStartOrderNumber, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompanyEndStop(TourStopRecord stop)
    {
        return string.Equals(stop.Id, TourStopIdentity.CompanyEndStopId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stop.Auftragsnummer, TourStopIdentity.CompanyEndOrderNumber, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFallbackStopId(TourStopRecord stop)
    {
        if (!string.IsNullOrWhiteSpace(stop.Auftragsnummer))
        {
            return $"auftrag:{stop.Auftragsnummer.Trim()}";
        }

        if (stop.Lat.HasValue && (stop.Lng.HasValue || stop.Lon.HasValue))
        {
            var lng = stop.Lng ?? stop.Lon ?? 0d;
            return $"coord:{Math.Round(stop.Lat.Value, 6)}:{Math.Round(lng, 6)}";
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string BuildCompanyAddress(AppSettings settings)
    {
        var street = (settings.CompanyStreet ?? string.Empty).Trim();
        var postalCode = (settings.CompanyPostalCode ?? string.Empty).Trim();
        var city = (settings.CompanyCity ?? string.Empty).Trim();
        var zipCity = string.Join(' ', new[] { postalCode, city }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var parts = new[] { street, zipCity }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return parts.Length == 0 ? "Firmenadresse nicht gesetzt" : string.Join(", ", parts);
    }

    private void AttachGlobalExceptionLogging()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            TryLogException("DispatcherUnhandledException", args.Exception);
            args.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                TryLogException("AppDomain.UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            TryLogException("TaskScheduler.UnobservedTaskException", args.Exception);
        };
    }

    private void TryLogException(string source, Exception ex)
    {
        try
        {
            var lines = new[]
            {
                "========================================",
                DateTimeOffset.Now.ToString("O"),
                source,
                ex.ToString(),
                string.Empty
            };
            File.AppendAllLines(_logPath, lines);
        }
        catch
        {
            // Ignore logging failures to avoid recursive crashes.
        }
    }
}
