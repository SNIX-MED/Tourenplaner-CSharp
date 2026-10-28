using Tourenplaner.CSharp.Application.Abstractions;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public class ImportResult
{
    public int CreatedOrders { get; set; }
    public int UpdatedOrders { get; set; }
    public int UnchangedOrders { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> ChangedOrderIds { get; set; } = new();
    public List<string> ProcessedOrderIds { get; set; } = new();
    public List<ImportTourAddressChange> TourAddressChanges { get; set; } = new();
    public DateTime ImportedAt { get; set; }
}

public interface IOrderImportService
{
    Task<ImportResult> ImportOrdersAsync(List<XmlOrderImportData> xmlOrders, IOrderRepository orderRepository,
        bool markAsXmlImported = false, IEnumerable<TourRecord>? tours = null);

    Task<ImportPreviewResult> PreviewImportAsync(List<XmlOrderImportData> xmlOrders,
        IOrderRepository orderRepository, IEnumerable<TourRecord>? tours = null);
}

public class OrderImportService : IOrderImportService
{
    private readonly OrderImportMapper _mapper = new();
    private readonly OrderDeliveryService _deliveryService = new();

    public async Task<ImportPreviewResult> PreviewImportAsync(List<XmlOrderImportData> xmlOrders,
        IOrderRepository orderRepository, IEnumerable<TourRecord>? tours = null)
    {
        var preview = new ImportPreviewResult { InputOrderCount = xmlOrders?.Count ?? 0 };
        var protectedOrderIds = GetActiveTourOrderIds(tours);
        var existingOrders = (await orderRepository.GetAllAsync()).ToList();
        var existingById = existingOrders
            .Where(order => !string.IsNullOrWhiteSpace(order.Id))
            .GroupBy(order => order.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var xmlOrder in xmlOrders ?? [])
        {
            try
            {
                var normalizedOrderId = (xmlOrder.AuftragNr ?? string.Empty).Trim();
                existingById.TryGetValue(normalizedOrderId, out var existingOrder);
                var importedOrder = PrepareOrder(xmlOrder, existingOrder, protectedOrderIds, false);
                ValidateDeliveryReconciliation(existingOrder, importedOrder);
                var difference = OrderImportDifferenceAnalyzer.Analyze(existingOrder, importedOrder);

                var item = BuildPreviewItem(existingOrder, importedOrder, difference);
                item.TourAddressChanges = FindTourAddressChanges(existingOrder, importedOrder, tours);
                foreach (var change in item.TourAddressChanges)
                    item.Changes.Insert(0, change.Description + ". Das Fahrtziel dieses eingeplanten Stopps wird geändert. Die Stoppreihenfolge bleibt erhalten.");
                preview.Items.Add(item);
                IncrementPreviewCount(preview, difference.Action);
            }
            catch (Exception ex)
            {
                preview.Errors.Add(BuildOrderErrorMessage(xmlOrder?.AuftragNr, ex));
            }
        }

        preview.Items = preview.Items
            .OrderBy(item => item.Action switch
            {
                ImportPreviewAction.Create => 0,
                ImportPreviewAction.Update => 1,
                _ => 2
            })
            .ToList();
        return preview;
    }

    public async Task<ImportResult> ImportOrdersAsync(List<XmlOrderImportData> xmlOrders,
        IOrderRepository orderRepository, bool markAsXmlImported = false,
        IEnumerable<TourRecord>? tours = null)
    {
        var result = new ImportResult { ImportedAt = DateTime.Now };
        var protectedOrderIds = GetActiveTourOrderIds(tours);
        var existingOrders = (await orderRepository.GetAllAsync()).ToList();
        var repairedPinMetadata = false;

        foreach (var xmlOrder in xmlOrders ?? [])
        {
            try
            {
                var existingOrder = existingOrders.FirstOrDefault(order =>
                    string.Equals(order.Id, xmlOrder.AuftragNr, StringComparison.OrdinalIgnoreCase));
                var importedOrder = PrepareOrder(xmlOrder, existingOrder, protectedOrderIds, markAsXmlImported);

                if (existingOrder is null)
                {
                    AddCreatedOrder(existingOrders, result, importedOrder);
                    continue;
                }

                if (RepairStaleManualPinReviewFlag(existingOrder, importedOrder))
                {
                    repairedPinMetadata = true;
                }

                var difference = OrderImportDifferenceAnalyzer.Analyze(existingOrder, importedOrder);
                if (difference.Action == ImportPreviewAction.Unchanged)
                {
                    result.UnchangedOrders++;
                    result.ProcessedOrderIds.Add(existingOrder.Id);
                    continue;
                }

                ReconcileDeliveries(existingOrder, importedOrder, applyChanges: true);
                result.TourAddressChanges.AddRange(FindTourAddressChanges(existingOrder, importedOrder, tours));
                ApplyImportedData(existingOrder, importedOrder);
                result.UpdatedOrders++;
                result.ProcessedOrderIds.Add(existingOrder.Id);
                AddChangedOrderId(result, existingOrder.Id);
            }
            catch (Exception ex)
            {
                result.Errors.Add(BuildOrderErrorMessage(xmlOrder?.AuftragNr, ex));
            }
        }

        if (result.CreatedOrders > 0 || result.UpdatedOrders > 0 || repairedPinMetadata)
        {
            await orderRepository.SaveAllAsync(existingOrders);
        }

        return result;
    }

    private Order PrepareOrder(XmlOrderImportData xmlOrder, Order? existingOrder,
        IReadOnlySet<string> protectedOrderIds, bool markAsXmlImported)
    {
        var importedOrder = _mapper.Map(xmlOrder, existingOrder, markAsXmlImported);
        if (existingOrder is not null)
        {
            PreserveActiveTourOrder(existingOrder, importedOrder, protectedOrderIds);
        }

        return importedOrder;
    }

    private void ValidateDeliveryReconciliation(Order? existingOrder, Order importedOrder)
    {
        if (existingOrder is not null)
        {
            ReconcileDeliveries(existingOrder, importedOrder, applyChanges: false);
        }
    }

    private void ReconcileDeliveries(Order existingOrder, Order importedOrder, bool applyChanges)
    {
        if (!_deliveryService.TryReconcileImportedProducts(existingOrder, importedOrder.Products,
                out var deliveryError, applyChanges))
        {
            throw new InvalidOperationException(
                $"Teillieferungen konnten nicht mit den XML-Produktmengen abgeglichen werden: {deliveryError}");
        }
    }

    private static ImportPreviewItem BuildPreviewItem(Order? existingOrder, Order importedOrder,
        OrderImportDifference difference) => new()
    {
        OrderId = existingOrder?.Id ?? importedOrder.Id,
        CustomerName = existingOrder is not null && string.IsNullOrWhiteSpace(importedOrder.CustomerName)
            ? existingOrder.CustomerName
            : importedOrder.CustomerName,
        Action = difference.Action,
        DeliveryType = importedOrder.DeliveryType,
        OrderTypeLabel = FormatOrderType(importedOrder.Type),
        Changes = difference.Changes.ToList()
    };

    private static void IncrementPreviewCount(ImportPreviewResult preview, ImportPreviewAction action)
    {
        switch (action)
        {
            case ImportPreviewAction.Create:
                preview.CreatedOrders++;
                break;
            case ImportPreviewAction.Update:
                preview.UpdatedOrders++;
                break;
            default:
                preview.UnchangedOrders++;
                break;
        }
    }

    private static void AddCreatedOrder(ICollection<Order> existingOrders, ImportResult result, Order createdOrder)
    {
        existingOrders.Add(createdOrder);
        result.CreatedOrders++;
        result.ProcessedOrderIds.Add(createdOrder.Id);
        AddChangedOrderId(result, createdOrder.Id);
    }

    private static void AddChangedOrderId(ImportResult result, string? orderId)
    {
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            result.ChangedOrderIds.Add(orderId);
        }
    }

    private static bool RepairStaleManualPinReviewFlag(Order existingOrder, Order importedOrder)
    {
        if (!existingOrder.IsLocationManuallySet || !existingOrder.ManualLocationRequiresReview ||
            OrderImportDifferenceAnalyzer.HasManualLocationAddressChanged(existingOrder, importedOrder))
        {
            return false;
        }

        existingOrder.ManualLocationRequiresReview = false;
        return true;
    }

    private static HashSet<string> GetActiveTourOrderIds(IEnumerable<TourRecord>? tours) =>
        (tours ?? [])
            .Where(tour => !tour.IsArchived)
            .SelectMany(tour => tour.Stops)
            .Where(stop => !TourStopIdentity.IsCompanyStop(stop) &&
                           !string.Equals(stop.StopKind?.Trim(), "pause", StringComparison.OrdinalIgnoreCase))
            .Select(stop => (stop.Auftragsnummer ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void PreserveActiveTourOrder(Order existingOrder, Order importedOrder,
        IReadOnlySet<string> protectedOrderIds)
    {
        var deliveryMethod = DeliveryMethodExtensions.ParseDeliveryMethod(importedOrder.DeliveryType);
        var normalizedDeliveryType = DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(importedOrder.DeliveryType);
        if (deliveryMethod == DeliveryMethodType.Selbstabholung ||
            string.Equals(normalizedDeliveryType, DeliveryMethodExtensions.Post, StringComparison.OrdinalIgnoreCase) ||
            deliveryMethod == DeliveryMethodType.Fracht_mit_Spediteur && !importedOrder.IsAlternativeDeliveryEnabled)
        {
            return;
        }

        if (importedOrder.IsArchived && protectedOrderIds.Contains(existingOrder.Id.Trim()))
        {
            importedOrder.IsArchived = existingOrder.IsArchived;
        }
    }

    private static void ApplyImportedData(Order existingOrder, Order importedOrder)
    {
        // Until geocoding succeeds, the previous coordinates must never describe the new address.
        if (!existingOrder.IsLocationManuallySet && HasDeliveryLocationChanged(existingOrder, importedOrder))
            existingOrder.Location = null;
        var manualLocationAddressChanged =
            OrderImportDifferenceAnalyzer.HasManualLocationAddressChanged(existingOrder, importedOrder);
        if (existingOrder.IsLocationManuallySet)
        {
            existingOrder.ManualLocationRequiresReview = manualLocationAddressChanged;
        }

        existingOrder.CustomerName = importedOrder.CustomerName;
        existingOrder.Address = importedOrder.Address;
        existingOrder.ScheduledDate = importedOrder.ScheduledDate;
        existingOrder.DeliveryDate = importedOrder.DeliveryDate;
        existingOrder.DeliveryPeriod = importedOrder.DeliveryPeriod;
        existingOrder.DeliveryCanOccurEarlier = importedOrder.DeliveryCanOccurEarlier;
        existingOrder.Type = importedOrder.Type;
        existingOrder.OrderAddress = importedOrder.OrderAddress;
        existingOrder.DeliveryAddress = importedOrder.DeliveryAddress;

        existingOrder.Email = importedOrder.Email;
        existingOrder.Phone = importedOrder.Phone;
        existingOrder.Products = importedOrder.Products;
        existingOrder.ManualTotalWeightKg = importedOrder.ManualTotalWeightKg;
        existingOrder.ManualTotalWeightInfo = importedOrder.ManualTotalWeightInfo;
        existingOrder.DeliveryType = importedOrder.DeliveryType;
        existingOrder.IsAlternativeDeliveryEnabled = importedOrder.IsAlternativeDeliveryEnabled;
        existingOrder.IsSelfPickupAlternativeEnabled = importedOrder.IsSelfPickupAlternativeEnabled;
        existingOrder.OrderStatus = importedOrder.OrderStatus;
        existingOrder.Notes = importedOrder.Notes;
        existingOrder.IstVorauszahlung = importedOrder.IstVorauszahlung;
        existingOrder.IstVorauszahlungBezahlt = importedOrder.IstVorauszahlungBezahlt;
        existingOrder.IsArchived = importedOrder.IsArchived;
        existingOrder.IsXmlImported = importedOrder.IsXmlImported;
    }

    private static List<ImportTourAddressChange> FindTourAddressChanges(Order? existing, Order imported,
        IEnumerable<TourRecord>? tours)
    {
        if (existing is null) return [];
        // Restoring the XML display address must also refresh already assigned tour stops.
        var manualSourceChanged = OrderImportDifferenceAnalyzer.HasManualLocationAddressChanged(existing, imported);
        static string Address(Order order) => OrderAddressFormatting.BuildAddressLine(
            order.DeliveryAddress?.Street, order.DeliveryAddress?.HouseNumber,
            order.DeliveryAddress?.PostalCode, order.DeliveryAddress?.City);
        var oldAddress = Address(existing);
        var newAddress = Address(imported);
        if (!manualSourceChanged && !HasDeliveryLocationChanged(existing, imported)) return [];
        return (tours ?? []).Where(t => !t.IsArchived && t.Stops.Any(s =>
                !TourStopIdentity.IsCompanyStop(s) &&
                string.Equals(s.Auftragsnummer?.Trim(), existing.Id.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Select(t => new ImportTourAddressChange(existing.Id, t.Id, t.Name, t.Date, oldAddress, newAddress)).ToList();
    }

    private static bool HasDeliveryLocationChanged(Order existing, Order imported)
    {
        static string Address(Order order) => OrderAddressFormatting.BuildAddressLine(order.DeliveryAddress?.Street,
            order.DeliveryAddress?.HouseNumber, order.DeliveryAddress?.PostalCode, order.DeliveryAddress?.City);
        return OrderAddressFormatting.NormalizeForComparison(Address(existing), OrderAddressComparisonProfile.XmlReimport) !=
            OrderAddressFormatting.NormalizeForComparison(Address(imported), OrderAddressComparisonProfile.XmlReimport);
    }

    private static string BuildOrderErrorMessage(string? orderId, Exception ex)
    {
        var normalizedOrderId = (orderId ?? string.Empty).Trim();
        return normalizedOrderId.Length == 0
            ? $"Fehler bei einem Auftrag ohne AuftragNr: {ex.Message}"
            : $"Fehler bei Auftrag {normalizedOrderId}: {ex.Message}";
    }

    private static string FormatOrderType(OrderType type) =>
        type == OrderType.Map ? "Karte" : "Nicht-Karte";
}
