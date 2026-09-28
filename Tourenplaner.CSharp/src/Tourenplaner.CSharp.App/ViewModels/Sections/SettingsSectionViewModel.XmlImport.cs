using System.IO;
using System.Windows;
using Microsoft.Win32;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed partial class SettingsSectionViewModel
{
    private int _xmlImportCheckedOrders;
    private int _xmlImportTotalOrders;

    public int XmlImportCheckedOrders
    {
        get => _xmlImportCheckedOrders;
        private set
        {
            SetProperty(ref _xmlImportCheckedOrders, value);
            RaiseImportProgressChanged();
        }
    }

    public int XmlImportTotalOrders
    {
        get => _xmlImportTotalOrders;
        private set
        {
            SetProperty(ref _xmlImportTotalOrders, value);
            RaiseImportProgressChanged();
        }
    }

    public double XmlImportProgressPercent => XmlImportTotalOrders == 0 ? 0 : 100.0 * XmlImportCheckedOrders / XmlImportTotalOrders;
    public bool IsXmlImportProgressIndeterminate => XmlImportTotalOrders == 0;
    public string XmlImportProgressText => XmlImportTotalOrders == 0
        ? "Aufträge werden importiert…"
        : $"{XmlImportCheckedOrders} von {XmlImportTotalOrders} Aufträgen geprüft";

    private void RaiseImportProgressChanged()
    {
        OnPropertyChanged(nameof(XmlImportProgressPercent));
        OnPropertyChanged(nameof(XmlImportProgressText));
        OnPropertyChanged(nameof(IsXmlImportProgressIndeterminate));
    }

    private Task<IReadOnlyList<TourRecord>> LoadToursForXmlImportAsync() =>
        (_tourRecordStore ?? throw new InvalidOperationException("Touren konnten für die Archivierungsprüfung nicht geladen werden."))
        .LoadAsync();

    private void DownloadXmlTemplateFile()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "XML-Dateien (*.xml)|*.xml",
            Title = "XML-Musterdatei speichern",
            FileName = "Auftragsimport-Muster.xml",
            DefaultExt = ".xml",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var xmlService = new XmlOrderImportService();
        File.WriteAllText(dialog.FileName, xmlService.CreateTemplateXml());
        ImportStatusMessage = $"Musterdatei gespeichert: {dialog.FileName}";
    }

    private async Task PreviewXmlImportAsync()
    {
        if (_orderRepository == null)
        {
            ImportStatusMessage = "Fehler: Auftragsrepository ist nicht initialisiert.";
            return;
        }

        IsPreviewingXmlImport = true;
        ImportStatusMessage = "XML-Datei wird geprüft...";

        try
        {
            if (string.IsNullOrWhiteSpace(XmlImportFilePath) || !File.Exists(XmlImportFilePath))
            {
                throw new FileNotFoundException("Bitte zuerst eine gültige XML-Datei auswählen.");
            }

            var fileInfo = new FileInfo(XmlImportFilePath);
            var xmlService = new XmlOrderImportService();
            var loadResult = xmlService.LoadOrdersFromFileDetailed(XmlImportFilePath, BuildXmlImportMapping());
            var importService = new OrderImportService();
            var tours = await LoadToursForXmlImportAsync();
            var preview = await importService.PreviewImportAsync(loadResult.Orders, _orderRepository, tours);

            var previewErrors = loadResult.Errors
                .Concat(preview.Errors)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var previewWarnings = loadResult.Warnings
                .Distinct(StringComparer.Ordinal)
                .ToList();

            ApplyXmlImportPreview(loadResult.Orders, preview, previewErrors, previewWarnings, fileInfo);

            var invalidCount = previewErrors.Count;
            ImportStatusMessage = BuildXmlImportPreviewStatusMessage(preview, invalidCount, previewWarnings.Count);
            StatusText = preview.ValidOrders > 0
                ? "XML Import Vorschau erstellt."
                : "XML Import Vorschau: keine gültigen Aufträge gefunden.";
        }
        catch (Exception ex)
        {
            ClearXmlImportPreview(clearStatus: false);
            ImportStatusMessage = $"Importvorschau fehlgeschlagen: {ex.Message}";
            StatusText = $"XML Import Vorschau fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsPreviewingXmlImport = false;
            RaiseXmlImportCommandStates();
        }
    }

    private async Task ImportOrdersAsync()
    {
        if (_orderRepository == null || _settingsRepository == null)
        {
            ImportStatusMessage = "Fehler: Repositories sind nicht initialisiert.";
            return;
        }

        if (!CanImportOrders())
        {
            ImportStatusMessage = "Bitte zuerst die XML-Datei prüfen. Wenn die Datei geändert wurde, erneut prüfen.";
            return;
        }

        XmlImportCheckedOrders = 0;
        XmlImportTotalOrders = 0;
        IsImportingOrders = true;
        ClearXmlImportPinIssues();
        ImportStatusMessage = "Importiere geprüfte Aufträge aus XML...";

        try
        {
            if (!IsCurrentPreviewFile())
            {
                throw new InvalidOperationException("Die XML-Datei wurde nach der Vorschau geändert. Bitte erneut prüfen.");
            }

            if (_previewedXmlOrders.Count == 0)
            {
                throw new InvalidOperationException("Es liegt keine gültige Importvorschau vor.");
            }

            var importService = new OrderImportService();
            var tours = await LoadToursForXmlImportAsync();
            var result = await importService.ImportOrdersAsync(
                _previewedXmlOrders.ToList(),
                _orderRepository,
                markAsXmlImported: true,
                tours: tours);

            var parserErrorCount = XmlImportPreviewErrors.Count;
            if (result.Errors.Any())
            {
                foreach (var error in result.Errors)
                {
                    if (!XmlImportPreviewErrors.Any(existing => string.Equals(existing, error, StringComparison.Ordinal)))
                    {
                        XmlImportPreviewErrors.Add(error);
                    }
                }

                RaiseXmlImportPreviewStateChanged();
            }

            var pinIssues = await EvaluateImportedPinAssignmentsAsync(result);
            ApplyXmlImportPinIssues(pinIssues);
            _dataSyncService?.PublishOrders(_instanceId);

            var appSettings = await _repository.LoadAsync();
            appSettings.XmlImportFilePath = XmlImportFilePath;
            appSettings.LastXmlImportDate = DateTime.Now;
            appSettings.XmlImportMapping = BuildXmlImportMapping().WithDefaults();
            await _repository.SaveAsync(appSettings);

            _hasPendingXmlImportPreview = false;
            RaiseXmlImportPreviewStateChanged();

            var totalErrorCount = parserErrorCount + result.Errors.Count;
            ImportStatusMessage = BuildXmlImportCompletionMessage(result, totalErrorCount, XmlImportPinIssueItems.Count);
            StatusText = $"XML Import abgeschlossen: {result.CreatedOrders} neu, {result.UpdatedOrders} aktualisiert, {result.UnchangedOrders} unverändert.";
            AppMessageBox.Show(
                ImportStatusMessage + Environment.NewLine + Environment.NewLine +
                (pinIssues.Count == 0 && totalErrorCount == 0
                    ? "Alle importierten Aufträge wurden geprüft."
                    : "Offene Prüfergebnisse finden Sie unten bei den Importwarnungen."),
                "XML-Import abgeschlossen", MessageBoxButton.OK,
                pinIssues.Count == 0 && totalErrorCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ImportStatusMessage = $"Importfehler: {ex.Message}";
            StatusText = $"XML Import fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsImportingOrders = false;
            RaiseXmlImportCommandStates();
        }
    }

    private bool CanImportOrders()
    {
        return !IsXmlImportBusy &&
               _hasPendingXmlImportPreview &&
               _previewedXmlOrders.Count > 0 &&
               IsCurrentPreviewFile();
    }

    private void ApplyXmlImportPreview(
        IReadOnlyList<XmlOrderImportData> previewOrders,
        ImportPreviewResult preview,
        IReadOnlyList<string> previewErrors,
        IReadOnlyList<string> previewWarnings,
        FileInfo fileInfo)
    {
        _previewedXmlOrders.Clear();
        _previewedXmlOrders.AddRange(previewOrders ?? []);
        _xmlImportPreviewLastWriteUtc = fileInfo.LastWriteTimeUtc;
        _xmlImportPreviewFileLength = fileInfo.Length;
        _hasPendingXmlImportPreview = _previewedXmlOrders.Count > 0;
        _xmlImportPreviewHiddenItemCount = Math.Max(0, preview.Items.Count - MaxXmlImportPreviewItems);
        XmlImportPreviewSummary = BuildXmlImportPreviewSummary(preview, previewErrors.Count, previewWarnings.Count);

        XmlImportPreviewItems.Clear();
        foreach (var item in preview.Items.Take(MaxXmlImportPreviewItems))
        {
            XmlImportPreviewItems.Add(XmlImportPreviewListItemViewModel.FromPreviewItem(item));
        }

        XmlImportPreviewErrors.Clear();
        foreach (var error in previewErrors)
        {
            XmlImportPreviewErrors.Add(error);
        }

        XmlImportPreviewWarnings.Clear();
        foreach (var warning in previewWarnings)
        {
            XmlImportPreviewWarnings.Add(warning);
        }

        RaiseXmlImportPreviewStateChanged();
    }

    private void ClearXmlImportPreview(bool clearStatus)
    {
        _previewedXmlOrders.Clear();
        _xmlImportPreviewLastWriteUtc = DateTime.MinValue;
        _xmlImportPreviewFileLength = 0;
        _xmlImportPreviewHiddenItemCount = 0;
        _hasPendingXmlImportPreview = false;
        XmlImportPreviewSummary = string.Empty;
        XmlImportPreviewItems.Clear();
        XmlImportPreviewErrors.Clear();
        XmlImportPreviewWarnings.Clear();
        XmlImportPinIssueItems.Clear();

        if (clearStatus)
        {
            ImportStatusMessage = string.Empty;
        }

        RaiseXmlImportPreviewStateChanged();
    }

    private async Task<IReadOnlyList<XmlImportPinIssueListItemViewModel>> EvaluateImportedPinAssignmentsAsync(ImportResult result)
    {
        if (_orderRepository is null)
        {
            return [];
        }

        var processedOrderIds = result.ProcessedOrderIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (processedOrderIds.Count == 0)
        {
            return [];
        }

        var allOrders = (await _orderRepository.GetAllAsync()).ToList();
        var importedOrders = allOrders
            .Where(x => processedOrderIds.Contains(x.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (importedOrders.Count == 0)
        {
            return [];
        }

        XmlImportTotalOrders = importedOrders.Count;
        var issues = new List<XmlImportPinIssueListItemViewModel>();
        var cacheFilePath = Path.Combine(_dataRoot, "geocode-cache.json");

        foreach (var order in importedOrders)
        {
            if (!DeliveryMethodExtensions.CanUseLiefertour(order))
            {
                XmlImportCheckedOrders++;
                continue;
            }
            ImportStatusMessage = $"Adressprüfung {XmlImportCheckedOrders + 1}/{XmlImportTotalOrders}: Auftrag {order.Id}…";
            var geocodingResolution = await AddressGeocodingService.TryResolveOrderWithDiagnosticsAsync(
                order, TomTomApiKey, cacheFilePath,
                message => ImportStatusMessage = $"Auftrag {order.Id}: {message}");
            var geocodingResult = geocodingResolution.Result;
            var nextLocation = geocodingResult?.IsPrecise == true
                ? geocodingResult.Location
                : null;
            if (nextLocation != order.Location)
            {
                // Preserve edits made while the network checks were in progress.
                var currentOrders = (await _orderRepository.GetAllAsync()).ToList();
                var current = currentOrders.FirstOrDefault(x => string.Equals(x.Id, order.Id, StringComparison.OrdinalIgnoreCase));
                if (current is null ||
                    !string.Equals(BuildXmlImportPinIssueAddress(current), BuildXmlImportPinIssueAddress(order), StringComparison.Ordinal) ||
                    current.Location != order.Location)
                {
                    AddChangedDuringCheckIssue(order);
                    XmlImportCheckedOrders++;
                    continue;
                }
                current.Location = nextLocation;
                try
                {
                    if (_orderMutationRepository is not null)
                        await _orderMutationRepository.UpsertAsync(current);
                    else
                        await _orderRepository.SaveAllAsync(currentOrders);
                }
                catch (ConcurrencyConflictException)
                {
                    AddChangedDuringCheckIssue(order);
                    XmlImportCheckedOrders++;
                    continue;
                }
            }

            XmlImportCheckedOrders++;
            var issue = CreateXmlImportPinIssue(order, geocodingResult, geocodingResolution.FailureReason);
            if (issue is not null)
            {
                issues.Add(issue);
            }
        }

        return issues;

        void AddChangedDuringCheckIssue(Order order) => issues.Add(XmlImportPinIssueListItemViewModel.CreateMissing(
            order.Id, order.CustomerName, BuildXmlImportPinIssueAddress(order),
            "Auftrag während der Prüfung geändert oder gelöscht. Bitte erneut prüfen.",
            EditXmlImportPinIssueOrderAsync, RecheckXmlImportPinIssueAsync,
            id => ShowXmlImportPinInfoAsync(id, BuildXmlImportPinIssueAddress(order), null, AddressGeocodingFailureReason.NoResult)));
    }

    private void ApplyXmlImportPinIssues(IReadOnlyList<XmlImportPinIssueListItemViewModel> issues)
    {
        XmlImportPinIssueItems.Clear();
        foreach (var issue in issues)
        {
            XmlImportPinIssueItems.Add(issue);
        }

        RaiseXmlImportPreviewStateChanged();
    }

    private void ClearXmlImportPinIssues()
    {
        XmlImportPinIssueItems.Clear();
        RaiseXmlImportPreviewStateChanged();
    }

    private XmlImportPinIssueListItemViewModel? CreateXmlImportPinIssue(
        Order order,
        AddressGeocodingResult? geocodingResult,
        AddressGeocodingFailureReason failureReason = AddressGeocodingFailureReason.NoResult)
    {
        var orderId = (order.Id ?? string.Empty).Trim();
        var customerName = (order.CustomerName ?? string.Empty).Trim();
        var addressLine = BuildXmlImportPinIssueAddress(order);
        if (geocodingResult is null)
        {
            return XmlImportPinIssueListItemViewModel.CreateMissing(
                orderId,
                customerName,
                addressLine,
                GetGeocodingFailureSummary(failureReason),
                EditXmlImportPinIssueOrderAsync,
                RecheckXmlImportPinIssueAsync,
                id => ShowXmlImportPinInfoAsync(id, addressLine, geocodingResult, failureReason));
        }

        if (!geocodingResult.IsPrecise)
        {
            return XmlImportPinIssueListItemViewModel.CreateApproximate(
                orderId,
                customerName,
                addressLine,
                (geocodingResult.MatchType ?? string.Empty).Trim(),
                geocodingResult.EntityType,
                EditXmlImportPinIssueOrderAsync,
                RecheckXmlImportPinIssueAsync,
                id => ShowXmlImportPinInfoAsync(id, addressLine, geocodingResult, failureReason));
        }

        return null;
    }

    private async Task ShowXmlImportPinInfoAsync(string orderId, string originalAddress,
        AddressGeocodingResult? result, AddressGeocodingFailureReason failureReason)
    {
        if (_orderRepository is null) return;
        var orders = (await _orderRepository.GetAllAsync()).ToList();
        var order = orders.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.OrdinalIgnoreCase));
        if (order is null)
        {
            RemoveXmlImportPinIssue(orderId);
            ImportStatusMessage = $"Auftrag {orderId} wurde nicht gefunden.";
            return;
        }

        // A warning may outlive an edit from another view or another user.
        if (!string.Equals(originalAddress, BuildXmlImportPinIssueAddress(order), StringComparison.Ordinal) ||
            (failureReason == AddressGeocodingFailureReason.MissingApiKey && !string.IsNullOrWhiteSpace(TomTomApiKey)))
        {
            originalAddress = BuildXmlImportPinIssueAddress(order);
            var resolution = await AddressGeocodingService.TryResolveOrderWithDiagnosticsAsync(
                order, TomTomApiKey, Path.Combine(_dataRoot, "geocode-cache.json"));
            result = resolution.Result;
            failureReason = resolution.FailureReason;
        }
        var dialog = new PinAddressInfoDialogWindow(order, originalAddress, result, GetGeocodingFailureSummary(failureReason))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true) return;
        if (!dialog.UseFoundAddress || result is null)
        {
            ImportStatusMessage = $"Auftrag {orderId}: ursprüngliche Adresse beibehalten. Die Pin-Zuordnung bleibt zur Prüfung offen.";
            return;
        }

        // The modal dialog pumps UI events; reload to preserve changes made while it was open.
        orders = (await _orderRepository.GetAllAsync()).ToList();
        order = orders.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.OrdinalIgnoreCase));
        if (order is null || !string.Equals(originalAddress, BuildXmlImportPinIssueAddress(order), StringComparison.Ordinal))
        {
            AppMessageBox.Show("Die Lieferadresse wurde zwischenzeitlich geändert oder der Auftrag gelöscht. Bitte Info erneut öffnen.",
                "Adresse geändert", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // Work on a copy: a failed save must not alter a shared repository object.
        var updated = System.Text.Json.JsonSerializer.Deserialize<Order>(
            System.Text.Json.JsonSerializer.Serialize(order))!;
        PinAddressComparison.ApplyFoundAddress(updated, result);
        try
        {
            if (_orderMutationRepository is not null)
                await _orderMutationRepository.UpsertAsync(updated);
            else
            {
                orders[orders.IndexOf(order)] = updated;
                await _orderRepository.SaveAllAsync(orders);
            }
        }
        catch (ConcurrencyConflictException)
        {
            AppMessageBox.Show("Der Auftrag wurde zwischenzeitlich geändert. Bitte Info erneut öffnen und die aktuelle Adresse prüfen.",
                "Mehrbenutzerkonflikt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _dataSyncService?.PublishOrders(_instanceId, updated.Id, updated.Id);
        RemoveXmlImportPinIssue(orderId);
        ImportStatusMessage = $"Auftrag {orderId}: gewählte Lieferadresse und geprüfte Kartenposition gespeichert.";
    }

    private async Task RecheckXmlImportPinIssueAsync(string orderId)
    {
        if (_orderRepository is null)
        {
            return;
        }

        var normalizedOrderId = (orderId ?? string.Empty).Trim();
        var orders = (await _orderRepository.GetAllAsync()).ToList();
        var order = orders.FirstOrDefault(x => string.Equals(x.Id, normalizedOrderId, StringComparison.OrdinalIgnoreCase));
        if (order is null)
        {
            RemoveXmlImportPinIssue(normalizedOrderId);
            ImportStatusMessage = $"Auftrag {normalizedOrderId} wurde nicht gefunden.";
            return;
        }

        var resolution = await AddressGeocodingService.TryResolveOrderWithDiagnosticsAsync(
            order,
            TomTomApiKey,
            Path.Combine(_dataRoot, "geocode-cache.json"));
        var result = resolution.Result;
        if (result?.IsPrecise == true && order.Location != result.Location)
        {
            order.Location = result.Location;
            await _orderRepository.SaveAllAsync(orders);
        }

        UpdateXmlImportPinIssue(normalizedOrderId, order, result, resolution.FailureReason);
        ImportStatusMessage = result?.IsPrecise == true
            ? $"Auftrag {normalizedOrderId} wurde erfolgreich erneut geprüft und zugeordnet."
            : $"Auftrag {normalizedOrderId}: {GetGeocodingFailureSummary(resolution.FailureReason)}";
    }

    private static string GetGeocodingFailureSummary(AddressGeocodingFailureReason reason) => reason switch
    {
        AddressGeocodingFailureReason.MissingApiKey => "TomTom-API-Key fehlt",
        AddressGeocodingFailureReason.AuthenticationFailed => "TomTom-Zugang wurde abgelehnt (API-Key prüfen)",
        AddressGeocodingFailureReason.RateLimited => "TomTom drosselt die Adresssuche weiterhin. Bitte sp\u00e4ter erneut pr\u00fcfen.",
        AddressGeocodingFailureReason.Timeout => "TomTom-Anfrage hat zu lange gedauert – bitte erneut prüfen",
        AddressGeocodingFailureReason.ConnectionFailed => "TomTom ist derzeit nicht erreichbar – Internetverbindung prüfen",
        AddressGeocodingFailureReason.InvalidServiceResponse => "TomTom hat eine ungültige Antwort geliefert – bitte erneut prüfen",
        AddressGeocodingFailureReason.ServiceUnavailable => "TomTom-Dienst ist derzeit nicht verfügbar – bitte erneut prüfen",
        _ => "Kein passender TomTom-Adresspunkt gefunden"
    };

    private async Task EditXmlImportPinIssueOrderAsync(string orderId)
    {
        if (_orderRepository is null)
        {
            ImportStatusMessage = "Fehler: Auftragsrepository ist nicht initialisiert.";
            return;
        }

        var normalizedOrderId = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedOrderId))
        {
            return;
        }

        var orders = (await _orderRepository.GetAllAsync()).ToList();
        var existing = orders.FirstOrDefault(x => string.Equals(x.Id, normalizedOrderId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            RemoveXmlImportPinIssue(normalizedOrderId);
            ImportStatusMessage = $"Auftrag {normalizedOrderId} wurde nicht gefunden.";
            return;
        }

        var dialog = new ManualOrderDialogWindow(
            existing,
            deliveryTypes: DeliveryMethodExtensions.AllDeliveryTypeOptions,
            defaultOrderType: existing.Type)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        var dialogResult = dialog.ShowDialog();
        if (dialog.DeleteRequested)
        {
            await DeleteXmlImportPinIssueOrderAsync(existing, orders);
            return;
        }

        if (dialogResult != true || dialog.CreatedOrder is null)
        {
            return;
        }

        var updated = dialog.CreatedOrder;
        updated.ConcurrencyToken = existing.ConcurrencyToken;
        var geocodingResult = await ApplyDeliveryMethodRoutingAsync(updated, existing.Location);

        var originalId = existing.Id;
        orders.RemoveAll(x => string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase));
        orders.RemoveAll(x => !string.Equals(x.Id, originalId, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(x.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        orders.Add(updated);

        try
        {
            if (!string.Equals(originalId, updated.Id, StringComparison.OrdinalIgnoreCase) &&
                _orderMutationRepository is not null)
            {
                await _orderMutationRepository.DeleteAsync(originalId, existing.ConcurrencyToken);
                updated.ConcurrencyToken = null;
                await _orderMutationRepository.UpsertAsync(updated);
            }
            else if (_orderMutationRepository is not null)
            {
                await _orderMutationRepository.UpsertAsync(updated);
            }
            else
            {
                await _orderRepository.SaveAllAsync(orders);
            }
        }
        catch (ConcurrencyConflictException)
        {
            AppMessageBox.Show(
                "Der Auftrag wurde zwischenzeitlich von einem anderen Benutzer geändert oder gelöscht. Bitte öffnen Sie den Auftrag erneut.",
                "Mehrbenutzerkonflikt",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var tours = (await LoadToursForXmlImportAsync()).ToList();
        var reconciliation = TourOrderReferenceService.ReconcileActiveToursWithOrders(tours, orders);
        if (reconciliation.HasChanges)
        {
            await _tourRecordStore!.SaveAsync(tours);
            _dataSyncService?.PublishTours(_instanceId);
        }

        _dataSyncService?.PublishOrders(_instanceId, originalId, updated.Id);
        UpdateXmlImportPinIssue(originalId, updated, geocodingResult);
        OrderPinAssignmentWarningService.ShowIfNeeded(updated, geocodingResult);
        ImportStatusMessage = $"Auftrag {updated.Id} wurde aktualisiert.";
    }

    private async Task<AddressGeocodingResult?> ApplyDeliveryMethodRoutingAsync(Order order, GeoPoint? fallbackLocation = null)
    {
        return await OrderDeliveryRoutingService.ApplyAsync(
            order,
            fallbackLocation,
            x => AddressGeocodingService.TryResolveOrderAsync(x, TomTomApiKey, Path.Combine(_dataRoot, "geocode-cache.json")),
            requirePreciseLocation: true);
    }

    private async Task DeleteXmlImportPinIssueOrderAsync(Order existing, List<Order> orders)
    {
        var confirmation = AppMessageBox.Show(
            $"Soll der Auftrag {existing.Id} wirklich gelöscht werden?",
            "Auftrag löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (_orderMutationRepository is not null)
            {
                await _orderMutationRepository.DeleteAsync(existing.Id, existing.ConcurrencyToken);
            }
            else
            {
                orders.RemoveAll(x => string.Equals(x.Id, existing.Id, StringComparison.OrdinalIgnoreCase));
                await _orderRepository!.SaveAllAsync(orders);
            }
        }
        catch (ConcurrencyConflictException)
        {
            AppMessageBox.Show(
                "Der Auftrag wurde zwischenzeitlich von einem anderen Benutzer geändert oder gelöscht. Bitte öffnen Sie den Auftrag erneut.",
                "Mehrbenutzerkonflikt",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _dataSyncService?.PublishOrders(_instanceId, existing.Id, null);
        RemoveXmlImportPinIssue(existing.Id);
        ImportStatusMessage = $"Auftrag {existing.Id} wurde gelöscht.";
    }

    private void UpdateXmlImportPinIssue(
        string originalOrderId,
        Order updatedOrder,
        AddressGeocodingResult? geocodingResult,
        AddressGeocodingFailureReason failureReason = AddressGeocodingFailureReason.NoResult)
    {
        var nextIssue = CreateXmlImportPinIssue(updatedOrder, geocodingResult, failureReason);
        var index = FindXmlImportPinIssueIndex(originalOrderId);
        if (index < 0)
        {
            index = FindXmlImportPinIssueIndex(updatedOrder.Id);
        }

        if (nextIssue is null)
        {
            if (index >= 0)
            {
                XmlImportPinIssueItems.RemoveAt(index);
            }
        }
        else if (index >= 0)
        {
            XmlImportPinIssueItems[index] = nextIssue;
        }
        else
        {
            XmlImportPinIssueItems.Add(nextIssue);
        }

        RaiseXmlImportPreviewStateChanged();
    }

    private void RemoveXmlImportPinIssue(string orderId)
    {
        var index = FindXmlImportPinIssueIndex(orderId);
        if (index >= 0)
        {
            XmlImportPinIssueItems.RemoveAt(index);
            RaiseXmlImportPreviewStateChanged();
        }
    }

    private int FindXmlImportPinIssueIndex(string? orderId)
    {
        var normalizedOrderId = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedOrderId))
        {
            return -1;
        }

        for (var i = 0; i < XmlImportPinIssueItems.Count; i++)
        {
            if (string.Equals(XmlImportPinIssueItems[i].OrderId, normalizedOrderId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void RaiseXmlImportPreviewStateChanged()
    {
        OnPropertyChanged(nameof(HasXmlImportPreview));
        OnPropertyChanged(nameof(HasXmlImportPreviewItems));
        OnPropertyChanged(nameof(HasXmlImportPreviewErrors));
        OnPropertyChanged(nameof(HasXmlImportPreviewWarnings));
        OnPropertyChanged(nameof(HasXmlImportPinIssues));
        OnPropertyChanged(nameof(HasXmlImportWarningsOrPinIssues));
        OnPropertyChanged(nameof(HasXmlImportPreviewHiddenItems));
        OnPropertyChanged(nameof(XmlImportPreviewHiddenItemsText));
        RaiseXmlImportCommandStates();
    }

    private void RaiseXmlImportCommandStates()
    {
        PreviewXmlImportCommand.RaiseCanExecuteChanged();
        ImportOrdersCommand.RaiseCanExecuteChanged();
    }

    private bool IsCurrentPreviewFile()
    {
        if (string.IsNullOrWhiteSpace(XmlImportFilePath) || !File.Exists(XmlImportFilePath))
        {
            return false;
        }

        if (_xmlImportPreviewLastWriteUtc == DateTime.MinValue)
        {
            return false;
        }

        var fileInfo = new FileInfo(XmlImportFilePath);
        return fileInfo.Length == _xmlImportPreviewFileLength &&
               fileInfo.LastWriteTimeUtc == _xmlImportPreviewLastWriteUtc;
    }

    private static string BuildXmlImportPreviewSummary(ImportPreviewResult preview, int invalidCount, int warningCount)
    {
        var parts = new List<string> { $"{preview.ValidOrders} Aufträge geprüft" };
        if (preview.CreatedOrders > 0) parts.Add($"{preview.CreatedOrders} neu");
        if (preview.UpdatedOrders > 0) parts.Add($"{preview.UpdatedOrders} geändert");
        if (preview.UnchangedOrders > 0) parts.Add($"{preview.UnchangedOrders} unverändert");
        if (invalidCount > 0) parts.Add($"{invalidCount} fehlerhaft");
        if (warningCount > 0) parts.Add($"{warningCount} Warnungen");
        return string.Join(" · ", parts);
    }

    private static string BuildXmlImportPreviewStatusMessage(ImportPreviewResult preview, int invalidCount, int warningCount)
    {
        if (preview.ValidOrders == 0)
        {
            return invalidCount > 0
                ? $"Keine gültigen Aufträge gefunden. {invalidCount} Eintrag/Einträge enthalten Fehler."
                : "Keine importierbaren Aufträge gefunden.";
        }

        var message = $"Vorschau erstellt: {preview.CreatedOrders} neue, {preview.UpdatedOrders} geänderte und {preview.UnchangedOrders} unveränderte Aufträge.";
        if (invalidCount > 0)
        {
            message += $" {invalidCount} Eintrag/Einträge werden wegen Fehlern übersprungen.";
        }

        if (warningCount > 0)
        {
            message += $" {warningCount} Warnung(en) bitte prüfen.";
        }

        return message;
    }

    private static string BuildXmlImportCompletionMessage(ImportResult result, int errorCount, int pinIssueCount)
    {
        var message = $"Import abgeschlossen: {result.CreatedOrders} neu, {result.UpdatedOrders} aktualisiert, {result.UnchangedOrders} unverändert.";
        if (result.CreatedOrders > 0 || result.UpdatedOrders > 0)
        {
            message += " Die Adressprüfung ist abgeschlossen.";
        }

        if (pinIssueCount > 0)
        {
            message += $" {pinIssueCount} importierte Karten-Aufträge sollten wegen unklarer Pin-Zuordnung manuell korrigiert werden.";
        }

        if (errorCount > 0)
        {
            message += $" {errorCount} Eintrag/Eintraege wurden wegen Fehlern nicht importiert.";
        }

        return message;
    }

    private static string BuildXmlImportPinIssueAddress(Order order)
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

        var addressLine = string.Join(", ", new[] { street, postalCity }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(addressLine))
        {
            return addressLine;
        }

        return (order.Address ?? string.Empty).Trim();
    }
}
