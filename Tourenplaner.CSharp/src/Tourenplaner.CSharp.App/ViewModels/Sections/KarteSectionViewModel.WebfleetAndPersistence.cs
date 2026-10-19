using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed partial class KarteSectionViewModel
{
    private Task SendCurrentTourToWebfleetAsync()
    {
        return SendCurrentTourToWebfleetCoreAsync(CancellationToken.None);
    }

    private async Task SendCurrentTourToWebfleetCoreAsync(CancellationToken cancellationToken)
    {
        if (_hasUnsavedRouteChanges)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                "Die Tour enthält noch ungespeicherte Änderungen. Bitte speichere die Tour zuerst, damit WEBFLEET exakt den aktuellen Stand mit Stoppreihenfolge und Zeiten erhält.",
                "WEBFLEET-Tour synchronisieren",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var tourId = ResolveCurrentTourId();
        var tours = (await _tourRepository.LoadAsync(cancellationToken)).ToList();
        var tour = tours.FirstOrDefault(x => x.Id == tourId);
        if (tour is null)
        {
            StatusText = "Bitte zuerst eine gespeicherte Tour auswählen.";
            return;
        }

        // Die aus dem Repository geladene Tour kann vor der Einführung der
        // Zeitfenster gespeichert worden sein. Vor jeder WEBFLEET-Synchronisierung
        // deshalb die Ankunftszeiten und die daraus abgeleitete Toleranz frisch
        // aus dem gespeicherten Fahrzeitprofil berechnen.
        _scheduleService.ApplySchedule(tour);

        var settings = await _settingsRepository.LoadAsync(cancellationToken);
        var webfleet = await ResolveWebfleetSettingsAsync(settings);
        if (!webfleet.IsEnabled || !webfleet.HasCredentials)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show("WEBFLEET ist noch nicht vollständig eingerichtet. Bitte zuerst unter Einstellungen > WEBFLEET aktivieren, API-Key und Passwort eingeben und die Verbindung testen.", "WEBFLEET", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var employees = await _employeeRepository.LoadAsync(cancellationToken);
        var driverCandidates = employees.Where(employee => (tour.EmployeeIds ?? []).Any(id => string.Equals(id, employee.Id, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(employee.WebfleetObjectUid)).ToList();
        if (driverCandidates.Count == 0)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show("Keinem der für die Tour zugeordneten Mitarbeiter ist ein WEBFLEET-Gerät zugeordnet.", "WEBFLEET-Fahrer prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var driver = driverCandidates[0];
        if (driverCandidates.Count > 1)
        {
            var dialog = new WebfleetDriverSelectionDialogWindow(driverCandidates) { Owner = System.Windows.Application.Current?.MainWindow };
            if (dialog.ShowDialog() != true || dialog.SelectedDriver is null) return;
            driver = dialog.SelectedDriver;
        }
        var vehicle = new Vehicle { Id = driver.Id, Name = driver.DisplayName, WebfleetObjectUid = driver.WebfleetObjectUid, WebfleetObjectNumber = driver.WebfleetObjectNumber };
        var dispatch = new WebfleetTourDispatchService();
        var errors = dispatch.Validate(tour, vehicle);
        if (errors.Count > 0)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(string.Join(Environment.NewLine, errors), "WEBFLEET-Versand prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var webfleetClient = new WebfleetConnectService();
        var synchronization = await dispatch.PreviewSynchronizationAsync(tour, vehicle!, webfleet, webfleetClient, cancellationToken);
        if (!synchronization.HasChanges)
        {
            StatusText = $"WEBFLEET: Alle {synchronization.UnchangedCount} Tourstopps sind bereits aktuell.";
            return;
        }

        var synchronizationMessage = $"Tour \"{tour.Name}\" mit \"{vehicle.Name}\" synchronisieren?{Environment.NewLine}{Environment.NewLine}" +
            $"Neu: {synchronization.CreateCount}{Environment.NewLine}" +
            $"Aktualisieren: {synchronization.UpdateCount}{Environment.NewLine}" +
            $"Unverändert: {synchronization.UnchangedCount}{Environment.NewLine}" +
            $"Aus WEBFLEET löschen: {synchronization.DeleteCount}";
        if (Tourenplaner.CSharp.App.Services.AppMessageBox.Show(synchronizationMessage, "WEBFLEET-Tour synchronisieren", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await dispatch.DispatchAsync(tour, vehicle!, webfleet, webfleetClient, cancellationToken);
        await _tourRepository.SaveAsync(tours, cancellationToken);
        StatusText = tour.WebfleetDispatch.LastMessage;
        await RefreshAfterLocalMutationAsync(tour.Id);
    }

    private Task ToggleWebfleetVehiclesAsync()
    {
        return ToggleWebfleetVehiclesCoreAsync(CancellationToken.None);
    }

    private async Task ToggleWebfleetVehiclesCoreAsync(CancellationToken cancellationToken)
    {
        if (_areWebfleetVehiclesVisible)
        {
            StopWebfleetVehicleAutoRefresh();
            _webfleetVehicles.Clear();
            _webfleetTrackLoadRevision++;
            _webfleetLiveTrack.Clear();
            _webfleetTrackWarning = null;
            _webfleetPauseMarkers.Clear();
            _areWebfleetVehiclesVisible = false;
            _webfleetVehicleRevision++;
            _webfleetTrackRevision++;
            OnPropertyChanged(nameof(WebfleetVehicleRevision));
            OnPropertyChanged(nameof(WebfleetTrackRevision));
            OnPropertyChanged(nameof(AreWebfleetVehiclesVisible));
            OnPropertyChanged(nameof(WebfleetVehiclesButtonToolTip));
            OnPropertyChanged(nameof(WebfleetVehiclesButtonImagePath));
            StatusText = "WEBFLEET-Fahrzeugpositionen ausgeblendet.";
            return;
        }

        await RefreshWebfleetVehiclesAsync(cancellationToken);
        if (_areWebfleetVehiclesVisible)
        {
            StartWebfleetVehicleAutoRefresh();
        }
    }

    private void StartWebfleetVehicleAutoRefresh()
    {
        StopWebfleetVehicleAutoRefresh();
        _webfleetVehicleRefreshCts = new CancellationTokenSource();
        RefreshWebfleetVehiclesPeriodicallyAsync(_webfleetVehicleRefreshCts.Token).Forget();
    }

    private void StopWebfleetVehicleAutoRefresh()
    {
        _webfleetVehicleRefreshCts?.Cancel();
        _webfleetVehicleRefreshCts?.Dispose();
        _webfleetVehicleRefreshCts = null;
    }

    private async Task RefreshWebfleetVehiclesPeriodicallyAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _areWebfleetVehiclesVisible)
        {
            try
            {
                var settings = await _settingsRepository.LoadAsync(cancellationToken);
                var webfleet = await ResolveWebfleetSettingsAsync(settings);
                var refreshSeconds = Math.Clamp(webfleet.PositionRefreshSeconds, 30, 3600);
                await Task.Delay(TimeSpan.FromSeconds(refreshSeconds), cancellationToken);
                if (cancellationToken.IsCancellationRequested || !_areWebfleetVehiclesVisible)
                {
                    break;
                }

                await RefreshWebfleetVehiclesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusText = $"WEBFLEET-Fahrzeugpositionen konnten nicht automatisch aktualisiert werden: {ex.Message}";
            }
        }
    }

    private async Task RefreshWebfleetVehiclesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.LoadAsync(cancellationToken);
        var webfleet = await ResolveWebfleetSettingsAsync(settings);
        if (!webfleet.IsEnabled || !webfleet.HasCredentials)
        {
            StatusText = "WEBFLEET ist noch nicht eingerichtet.";
            return;
        }
        var vehicles = await new WebfleetConnectService().GetVehiclesAsync(webfleet, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _webfleetVehicles.Clear();
        _webfleetVehicles.AddRange(vehicles.Where(x => x.Latitude.HasValue && x.Longitude.HasValue));
        _areWebfleetVehiclesVisible = true;
        _webfleetVehicleRevision++;
        OnPropertyChanged(nameof(WebfleetVehicleRevision));
        OnPropertyChanged(nameof(AreWebfleetVehiclesVisible));
        OnPropertyChanged(nameof(WebfleetVehiclesButtonToolTip));
        OnPropertyChanged(nameof(WebfleetVehiclesButtonImagePath));
        StatusText = $"WEBFLEET: {_webfleetVehicles.Count} Fahrzeugposition(en) aktualisiert.";
        var trackedVehicle = _webfleetVehicles.FirstOrDefault(x => string.Equals(x.ObjectUid, _webfleetLiveTrack.ObjectUid, StringComparison.OrdinalIgnoreCase));
        if (trackedVehicle is not null && _webfleetLiveTrack.Append(trackedVehicle, DateTimeOffset.Now))
        {
            _webfleetTrackRevision++;
            OnPropertyChanged(nameof(WebfleetTrackRevision));
            PublishWebfleetTrackStatus();
        }
        if (!_webfleetTrackHistoryRefreshRunning && _webfleetLiveTrack.TryBeginHistoryRefresh(DateTimeOffset.Now))
        {
            RefreshLiveWebfleetTrackHistoryAsync(webfleet, cancellationToken).Forget();
        }
    }

    private async Task RefreshLiveWebfleetTrackHistoryAsync(WebfleetConnectionSettings settings, CancellationToken cancellationToken)
    {
        _webfleetTrackHistoryRefreshRunning = true;
        var generation = _webfleetLiveTrack.Generation;
        var objectUid = _webfleetLiveTrack.ObjectUid;
        var date = _webfleetLiveTrack.Date;
        try
        {
            var from = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
            var points = await new WebfleetConnectService().GetTrackAsync(settings, objectUid, from, from.AddDays(1).AddTicks(-1), cancellationToken);
            if (cancellationToken.IsCancellationRequested || !_webfleetLiveTrack.IsToday(DateTimeOffset.Now) ||
                !_webfleetLiveTrack.MergeHistory(generation, points)) return;
            _webfleetTrackRevision++;
            OnPropertyChanged(nameof(WebfleetTrackRevision));
            PublishWebfleetTrackStatus();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Retain the displayed track and continue appending live positions on history failure.
            if (generation == _webfleetLiveTrack.Generation)
                StatusText = $"WEBFLEET-Verlauf konnte nicht nachgeladen werden; Live-Positionen werden weiter ergänzt: {ex.Message}";
        }
        finally
        {
            _webfleetTrackHistoryRefreshRunning = false;
        }
    }

    private async Task SaveSelectedTourOverviewStartTimeAsync(
        int tourId,
        bool suppressNoChangeStatus = false,
        bool suppressSuccessStatus = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tours = (await _tourRepository.LoadAsync(cancellationToken)).ToList();
            var tour = tours.FirstOrDefault(x => x.Id == tourId);
            if (tour is null)
            {
                Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                    "Die ausgewählte Tour wurde nicht gefunden.",
                    "Tour speichern",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                await RefreshAsync();
                return;
            }

            var updatedStartTime = RouteStartTime;
            if (string.Equals((tour.StartTime ?? string.Empty).Trim(), updatedStartTime, StringComparison.Ordinal))
            {
                SetRouteChanged(false);
                if (!suppressNoChangeStatus)
                {
                    StatusText = "Keine Änderungen zum Speichern.";
                }
                return;
            }

            tour.StartTime = updatedStartTime;
            await SaveTourRecordAsync(tour, tours, cancellationToken);
            _dataSyncService.PublishTours(_instanceId, tourId.ToString(CultureInfo.InvariantCulture), tourId.ToString(CultureInfo.InvariantCulture));
            await LoadSavedToursAsync(tourId);
            SetRouteChanged(false);
            if (!suppressSuccessStatus)
            {
                StatusText = $"Startzeit für {BuildTourLookupLabel(tour)} gespeichert.";
            }
        }
        catch (ConcurrencyConflictException conflict)
        {
            StatusText = "Tour wurde wegen eines Mehrbenutzer-Konflikts nicht gespeichert.";
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                $"Die Tour wurde nicht gespeichert. Die Daten werden neu geladen.\n\n{conflict.Message}",
                "Mehrbenutzer-Konflikt",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            await RefreshAsync();
        }
        catch (IOException ioEx)
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                $"Die Tour konnte nicht gespeichert werden, weil eine Datendatei gerade gesperrt ist.\n\nDetails: {ioEx.Message}",
                "Speichern fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText = "Speichern fehlgeschlagen: Datendatei gesperrt.";
        }
    }

    private Task SaveTourRecordAsync(
        TourRecord tour,
        IReadOnlyList<TourRecord> fallbackSnapshot,
        CancellationToken cancellationToken = default)
    {
        return _tourMutationStore is not null
            ? _tourMutationStore.UpsertAsync(tour, cancellationToken)
            : _tourRepository.SaveAsync(fallbackSnapshot, cancellationToken);
    }

    private async Task SaveToursAndOrdersAsync(
        IReadOnlyList<TourRecord> tours,
        IReadOnlyList<Order> orders,
        IReadOnlyList<TourRecord>? toursToDelete = null,
        IReadOnlyList<Order>? ordersToDelete = null,
        CancellationToken cancellationToken = default)
    {
        if (_tourOrderTransactionStore is not null)
        {
            await _tourOrderTransactionStore.SaveAsync(tours, orders, toursToDelete, ordersToDelete, cancellationToken);
            return;
        }

        await _tourRepository.SaveAsync(tours, cancellationToken);
        await _orderRepository.SaveAllAsync(orders, cancellationToken);
    }

    private Task SaveChangedToursAndOrdersAsync(
        IReadOnlyList<TourRecord> originalTours,
        IReadOnlyList<TourRecord> currentTours,
        IReadOnlyList<Order> originalOrders,
        IReadOnlyList<Order> currentOrders,
        CancellationToken cancellationToken = default)
    {
        return _tourOrderTransactionStore is not null
            ? SaveToursAndOrdersAsync(
                SelectChangedItems(originalTours, currentTours, x => x.Id.ToString(CultureInfo.InvariantCulture)),
                SelectChangedItems(originalOrders, currentOrders, x => x.Id),
                cancellationToken: cancellationToken)
            : SaveToursAndOrdersAsync(currentTours, currentOrders, cancellationToken: cancellationToken);
    }

    private static List<T> CloneItems<T>(IEnumerable<T> items)
        => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(items ?? [])) ?? [];

    private static IReadOnlyList<T> SelectChangedItems<T>(
        IEnumerable<T> originalItems,
        IEnumerable<T> currentItems,
        Func<T, string> keySelector)
    {
        var originals = (originalItems ?? []).ToDictionary(keySelector, StringComparer.OrdinalIgnoreCase);
        return (currentItems ?? [])
            .Where(item => !originals.TryGetValue(keySelector(item), out var original) || !AreEquivalentWithoutConcurrencyToken(original, item))
            .ToList();
    }

    private static bool AreEquivalentWithoutConcurrencyToken<T>(T left, T right)
    {
        static string Serialize(T value)
        {
            var node = JsonSerializer.SerializeToNode(value) as JsonObject;
            node?.Remove("ConcurrencyToken");
            node?.Remove("concurrencyToken");
            return node?.ToJsonString() ?? string.Empty;
        }

        return string.Equals(Serialize(left), Serialize(right), StringComparison.Ordinal);
    }

}
