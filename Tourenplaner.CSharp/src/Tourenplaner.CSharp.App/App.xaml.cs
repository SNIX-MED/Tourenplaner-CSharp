using System.IO;
using System.Linq;
using System.Globalization;
using System.Windows;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.App;

public partial class App : System.Windows.Application
{
    private string _logPath = string.Empty;
    private AppDataHistoryService? _historyService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tourenplaner.CSharp",
            "data");

        Directory.CreateDirectory(dataRoot);
        _logPath = Path.Combine(dataRoot, "app-crash.log");
        AttachGlobalExceptionLogging();

        try
        {
            var settingsPath = Path.Combine(dataRoot, "settings.json");
            var ordersJsonPath = Path.Combine(dataRoot, "orders.json");
            var toursJsonPath = Path.Combine(dataRoot, "tours.json");
            var employeesJsonPath = Path.Combine(dataRoot, "employees.json");
            var vehiclesJsonPath = Path.Combine(dataRoot, "vehicles.json");

            var dataSyncService = new AppDataSyncService();
            var historyService = new AppDataHistoryService(
                dataSyncService,
                ordersJsonPath,
                toursJsonPath,
                employeesJsonPath,
                vehiclesJsonPath,
                settingsPath);
            var mainWindow = new MainWindow
            {
                DataContext = new MainShellViewModel(
                    historyService,
                    dataSyncService,
                    ordersJsonPath,
                    toursJsonPath,
                    employeesJsonPath,
                    vehiclesJsonPath,
                    settingsPath,
                    dataRoot)
            };

            historyService.Initialize();
            _historyService = historyService;
            await RunTourIntegrityCheckOnStartup(toursJsonPath, settingsPath);
            mainWindow.Show();
            await PromptPastTourArchivingOnStartupAsync(mainWindow, toursJsonPath, ordersJsonPath);
        }
        catch (Exception ex)
        {
            TryLogException("StartupFailed", ex);
            MessageBox.Show(
                $"Die App konnte nicht gestartet werden.{Environment.NewLine}{Environment.NewLine}" +
                $"Fehler: {ex.Message}{Environment.NewLine}{Environment.NewLine}" +
                $"Details: {_logPath}",
                "GAWELA Tourenplaner - Startfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _historyService?.Dispose();
        _historyService = null;
        base.OnExit(e);
    }

    private async Task PromptPastTourArchivingOnStartupAsync(Window owner, string toursJsonPath, string ordersJsonPath)
    {
        try
        {
            var toursRepository = new JsonToursRepository(toursJsonPath);
            var orderRepository = new JsonOrderRepository(ordersJsonPath);

            var tours = (await toursRepository.LoadAsync()).ToList();
            var orders = (await orderRepository.GetAllAsync()).ToList();

            var pastTours = tours
                .Where(t => !t.IsArchived && TryParseTourDate(t.Date, out var parsedDate) && parsedDate < DateTime.Today)
                .OrderBy(t => ParseTourDateOrToday(t.Date))
                .ThenBy(t => t.Id)
                .ToList();

            if (pastTours.Count == 0)
            {
                return;
            }

            var confirm = MessageBox.Show(
                owner,
                $"Es wurden {pastTours.Count} vergangene Tour(en) gefunden.{Environment.NewLine}{Environment.NewLine}" +
                "Sollen diese inklusive Aufträge archiviert werden?",
                "Vergangene Touren archivieren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var toursChanged = false;
            var ordersChanged = false;
            var userCanceled = false;

            for (var index = 0; index < pastTours.Count; index++)
            {
                var tour = pastTours[index];
                var tourKey = tour.Id.ToString(CultureInfo.InvariantCulture);
                var tourDate = ParseTourDateOrToday(tour.Date);
                var candidateOrders = orders
                    .Where(o => !o.IsArchived &&
                                string.Equals((o.AssignedTourId ?? string.Empty).Trim(), tourKey, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var dialog = new StartupTourArchiveDialogWindow(
                    tour,
                    tourDate,
                    index + 1,
                    pastTours.Count,
                    candidateOrders)
                {
                    Owner = owner
                };

                var result = dialog.ShowDialog();
                if (result != true)
                {
                    if (dialog.CancelAll)
                    {
                        userCanceled = true;
                        break;
                    }

                    continue;
                }

                if (!dialog.ShouldArchiveTour)
                {
                    continue;
                }

                tour.IsArchived = true;
                toursChanged = true;

                var selectedOrderIds = new HashSet<string>(dialog.SelectedOrderIds, StringComparer.OrdinalIgnoreCase);
                foreach (var order in candidateOrders)
                {
                    if (selectedOrderIds.Contains(order.Id))
                    {
                        if (!order.IsArchived)
                        {
                            order.IsArchived = true;
                            ordersChanged = true;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(order.AssignedTourId))
                    {
                        order.AssignedTourId = string.Empty;
                        ordersChanged = true;
                    }
                }
            }

            if (toursChanged)
            {
                await toursRepository.SaveAsync(tours);
            }

            if (ordersChanged)
            {
                await orderRepository.SaveAllAsync(orders);
            }

            if (toursChanged || ordersChanged)
            {
                var scope = userCanceled ? "teilweise" : "vollständig";
                TryLogInfo(
                    "PromptPastTourArchivingOnStartupAsync",
                    $"Vergangene Touren wurden {scope} archiviert. Touren geändert: {toursChanged}, Aufträge geändert: {ordersChanged}.");
            }
        }
        catch (Exception ex)
        {
            TryLogException("PromptPastTourArchivingOnStartupAsync", ex);
        }
    }

    private async Task RunTourIntegrityCheckOnStartup(string toursJsonPath, string settingsJsonPath)
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
                return;
            }

            var backupPath = CreateStartupRepairBackup(toursJsonPath);
            await toursRepository.SaveAsync(tours);
            TryLogInfo(
                "RunTourIntegrityCheckOnStartup",
                $"Tour-Integritätsprüfung hat {touchedTours} Tour(en) aktualisiert. Backup: {backupPath}. " +
                $"Startstopps eingefügt: {insertedStart}, Endstopps eingefügt: {insertedEnd}, " +
                $"Pflichtfelder repariert: {repairedRequiredFields}, doppelte Firmenstopps entfernt: {removedDuplicateCompanyStops}, " +
                $"Touren neu indexiert: {reindexedTours}.");
            return;
        }
        catch (Exception ex)
        {
            TryLogException("RunTourIntegrityCheckOnStartup", ex);
            return;
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

    private static bool TryParseTourDate(string? value, out DateTime date)
    {
        var input = (value ?? string.Empty).Trim();
        return DateTime.TryParseExact(input, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
               DateTime.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static DateTime ParseTourDateOrToday(string? value)
    {
        return TryParseTourDate(value, out var parsed) ? parsed : DateTime.Today;
    }

    private static string CreateStartupRepairBackup(string toursJsonPath)
    {
        if (!File.Exists(toursJsonPath))
        {
            throw new FileNotFoundException("Die Tourdatei für die Startup-Reparatur wurde nicht gefunden.", toursJsonPath);
        }

        var source = new FileInfo(toursJsonPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(
            source.DirectoryName ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(source.Name)}.startup-repair_{timestamp}{source.Extension}.bak");

        File.Copy(toursJsonPath, backupPath, overwrite: false);
        return backupPath;
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

    private void TryLogInfo(string source, string message)
    {
        try
        {
            var lines = new[]
            {
                "========================================",
                DateTimeOffset.Now.ToString("O"),
                source,
                message,
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



