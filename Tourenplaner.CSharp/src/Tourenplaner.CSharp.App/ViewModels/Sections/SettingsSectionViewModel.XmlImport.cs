using System.IO;
using System.Windows;
using Microsoft.Win32;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed partial class SettingsSectionViewModel
{
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
            var importService = new SqlOrderImportService();
            var preview = await importService.PreviewImportAsync(loadResult.Orders, _orderRepository);

            var previewErrors = loadResult.Errors
                .Concat(preview.Errors)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            ApplyXmlImportPreview(loadResult.Orders, preview, previewErrors, fileInfo);

            var invalidCount = previewErrors.Count;
            ImportStatusMessage = BuildXmlImportPreviewStatusMessage(preview, invalidCount);
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

        IsImportingOrders = true;
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

            var importService = new SqlOrderImportService();
            var result = await importService.ImportOrdersAsync(
                _previewedXmlOrders.ToList(),
                _orderRepository,
                _settingsRepository);

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

            if (result.CreatedOrders > 0 || result.UpdatedOrders > 0)
            {
                var pinIssues = await EvaluateImportedPinAssignmentsAsync(result);
                ApplyXmlImportPinIssues(pinIssues);
                _dataSyncService?.PublishOrders(_instanceId);
                StartBackgroundPinGeocoding();

                if (pinIssues.Count > 0)
                {
                    AppMessageBox.Show(
                        $"XML-Import abgeschlossen.{Environment.NewLine}{Environment.NewLine}" +
                        $"{pinIssues.Count} importierte Karten-Auftraege konnten nicht exakt zugeordnet werden.{Environment.NewLine}" +
                        "Die betroffenen Auftraege sind unten im Bereich \"Problematische Pin-Zuordnungen\" aufgelistet.",
                        "Pins pruefen",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else
            {
                ClearXmlImportPinIssues();
            }

            var appSettings = await _settingsRepository.GetAsync();
            appSettings.XmlImportFilePath = XmlImportFilePath;
            appSettings.LastXmlImportDate = DateTime.Now;
            appSettings.XmlImportMapping = BuildXmlImportMapping().WithDefaults();
            await _settingsRepository.SaveAsync(appSettings);

            _hasPendingXmlImportPreview = false;
            RaiseXmlImportPreviewStateChanged();

            var totalErrorCount = parserErrorCount + result.Errors.Count;
            ImportStatusMessage = BuildXmlImportCompletionMessage(result, totalErrorCount, XmlImportPinIssueItems.Count);
            StatusText = $"XML Import abgeschlossen: {result.CreatedOrders} neu, {result.UpdatedOrders} aktualisiert, {result.UnchangedOrders} unverändert.";
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
        IReadOnlyList<SqlOrderImportData> previewOrders,
        ImportPreviewResult preview,
        IReadOnlyList<string> previewErrors,
        FileInfo fileInfo)
    {
        _previewedXmlOrders.Clear();
        _previewedXmlOrders.AddRange(previewOrders ?? []);
        _xmlImportPreviewLastWriteUtc = fileInfo.LastWriteTimeUtc;
        _xmlImportPreviewFileLength = fileInfo.Length;
        _hasPendingXmlImportPreview = _previewedXmlOrders.Count > 0;
        _xmlImportPreviewHiddenItemCount = Math.Max(0, preview.Items.Count - MaxXmlImportPreviewItems);
        XmlImportPreviewSummary = BuildXmlImportPreviewSummary(preview, previewErrors.Count);

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
        XmlImportPinIssueSummary = string.Empty;
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

        var changedOrderIds = result.ChangedOrderIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (changedOrderIds.Count == 0)
        {
            return [];
        }

        var allOrders = (await _orderRepository.GetAllAsync()).ToList();
        var importedMapOrders = allOrders
            .Where(x => x.Type == OrderType.Map &&
                        changedOrderIds.Contains(x.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (importedMapOrders.Count == 0)
        {
            return [];
        }

        var issues = new List<XmlImportPinIssueListItemViewModel>();
        var cacheFilePath = Path.Combine(_dataRoot, "geocode-cache.json");
        var hasLocationUpdates = false;

        foreach (var order in importedMapOrders)
        {
            var geocodingResult = await AddressGeocodingService.TryResolveOrderAsync(order, TomTomApiKey, cacheFilePath);
            if (geocodingResult?.Location != order.Location)
            {
                order.Location = geocodingResult?.Location;
                hasLocationUpdates = true;
            }

            var addressLine = BuildXmlImportPinIssueAddress(order);
            if (geocodingResult is null)
            {
                issues.Add(XmlImportPinIssueListItemViewModel.CreateMissing(
                    (order.Id ?? string.Empty).Trim(),
                    (order.CustomerName ?? string.Empty).Trim(),
                    addressLine));
                continue;
            }

            if (!geocodingResult.IsPrecise)
            {
                issues.Add(XmlImportPinIssueListItemViewModel.CreateApproximate(
                    (order.Id ?? string.Empty).Trim(),
                    (order.CustomerName ?? string.Empty).Trim(),
                    addressLine,
                    (geocodingResult.MatchType ?? string.Empty).Trim(),
                    geocodingResult.EntityType));
            }
        }

        if (hasLocationUpdates)
        {
            await _orderRepository.SaveAllAsync(allOrders);
        }

        return issues;
    }

    private void ApplyXmlImportPinIssues(IReadOnlyList<XmlImportPinIssueListItemViewModel> issues)
    {
        XmlImportPinIssueItems.Clear();
        foreach (var issue in issues)
        {
            XmlImportPinIssueItems.Add(issue);
        }

        XmlImportPinIssueSummary = issues.Count == 0
            ? string.Empty
            : $"{issues.Count} importierte Karten-Auftraege muessen bei der Pin-Zuordnung manuell geprueft werden.";
        RaiseXmlImportPreviewStateChanged();
    }

    private void ClearXmlImportPinIssues()
    {
        XmlImportPinIssueSummary = string.Empty;
        XmlImportPinIssueItems.Clear();
        RaiseXmlImportPreviewStateChanged();
    }

    private void RaiseXmlImportPreviewStateChanged()
    {
        OnPropertyChanged(nameof(HasXmlImportPreview));
        OnPropertyChanged(nameof(HasXmlImportPreviewItems));
        OnPropertyChanged(nameof(HasXmlImportPreviewErrors));
        OnPropertyChanged(nameof(HasXmlImportPinIssues));
        OnPropertyChanged(nameof(HasXmlImportPinIssueSummary));
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

    private static string BuildXmlImportPreviewSummary(ImportPreviewResult preview, int invalidCount)
    {
        return $"{preview.ValidOrders} gültige Aufträge geprüft | " +
               $"{preview.CreatedOrders} neu | " +
               $"{preview.UpdatedOrders} mit Änderungen | " +
               $"{preview.UnchangedOrders} unverändert | " +
               $"{invalidCount} fehlerhaft";
    }

    private static string BuildXmlImportPreviewStatusMessage(ImportPreviewResult preview, int invalidCount)
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

        return message;
    }

    private static string BuildXmlImportCompletionMessage(ImportResult result, int errorCount, int pinIssueCount)
    {
        var message = $"Import abgeschlossen: {result.CreatedOrders} neu, {result.UpdatedOrders} aktualisiert, {result.UnchangedOrders} unverändert.";
        if (result.CreatedOrders > 0 || result.UpdatedOrders > 0)
        {
            message += " Pins ohne Koordinaten werden im Hintergrund weiter geprüft.";
        }

        if (pinIssueCount > 0)
        {
            message += $" {pinIssueCount} importierte Karten-Auftraege sollten wegen unklarer Pin-Zuordnung manuell korrigiert werden.";
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
