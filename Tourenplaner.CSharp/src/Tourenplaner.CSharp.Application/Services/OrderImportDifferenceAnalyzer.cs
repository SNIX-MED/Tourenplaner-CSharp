using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed record OrderImportDifference(
    ImportPreviewAction Action,
    IReadOnlyList<string> Changes);

public static class OrderImportDifferenceAnalyzer
{
    public static OrderImportDifference Analyze(Order? existingOrder, Order importedOrder)
    {
        ArgumentNullException.ThrowIfNull(importedOrder);

        if (existingOrder is null)
        {
            return new OrderImportDifference(
                ImportPreviewAction.Create,
                ["Neuer Auftrag wird angelegt."]);
        }

        var changes = DescribeDifferences(existingOrder, importedOrder);
        return new OrderImportDifference(
            changes.Count == 0 ? ImportPreviewAction.Unchanged : ImportPreviewAction.Update,
            changes);
    }

    public static List<string> DescribeDifferences(Order existingOrder, Order importedOrder)
    {
        ArgumentNullException.ThrowIfNull(existingOrder);
        ArgumentNullException.ThrowIfNull(importedOrder);

        var changes = new List<string>();

        AddChange(changes, "Kunde", existingOrder.CustomerName, importedOrder.CustomerName);
        AddChange(changes, "Termin", FormatDate(existingOrder.ScheduledDate), FormatDate(importedOrder.ScheduledDate));
        AddChange(changes, "Lieferdatum", existingOrder.DeliveryDate?.ToString("dd.MM.yyyy"), importedOrder.DeliveryDate?.ToString("dd.MM.yyyy"));
        AddChange(changes, "Lieferzeitraum", existingOrder.DeliveryPeriod, importedOrder.DeliveryPeriod);
        AddChange(changes, "Lieferung kann früher erfolgen", FormatBool(existingOrder.DeliveryCanOccurEarlier), FormatBool(importedOrder.DeliveryCanOccurEarlier));
        AddChange(changes, "Typ", FormatOrderType(existingOrder.Type), FormatOrderType(importedOrder.Type));
        AddChange(changes, "Lieferart", existingOrder.DeliveryType, importedOrder.DeliveryType);
        if (existingOrder.IsAlternativeDeliveryEnabled && !importedOrder.IsAlternativeDeliveryEnabled)
        {
            changes.Add($"Alternative Planung: {DeliveryMethodExtensions.GetAlternativeDeliveryLabel(existingOrder.DeliveryType)} -> zurückgesetzt");
        }

        if (existingOrder.IsSelfPickupAlternativeEnabled && !importedOrder.IsSelfPickupAlternativeEnabled)
        {
            changes.Add("Evtl. Selbstabholung -> zurückgesetzt");
        }

        AddOrderAddressChanges(changes, existingOrder.OrderAddress, importedOrder.OrderAddress);
        var manualLocationAddressChanged = HasManualLocationAddressChanged(existingOrder, importedOrder);
        AddDeliveryAddressChanges(changes, existingOrder.DeliveryAddress, importedOrder.DeliveryAddress);

        if (manualLocationAddressChanged)
        {
            changes.Add("Manueller Pin bleibt erhalten; geänderte Lieferadresse muss geprüft werden.");
        }

        AddChange(changes, "E-Mail", existingOrder.Email, importedOrder.Email);
        AddChange(changes, "Telefon", existingOrder.Phone, importedOrder.Phone);
        AddChange(changes, "Notiz", existingOrder.Notes, importedOrder.Notes);
        AddChange(changes, "Vorauskasse", FormatBool(existingOrder.IstVorauszahlung), FormatBool(importedOrder.IstVorauszahlung));
        AddChange(changes, "Vorauszahlung bezahlt", FormatBool(existingOrder.IstVorauszahlungBezahlt), FormatBool(importedOrder.IstVorauszahlungBezahlt));
        AddChange(changes, "Archiviert", FormatBool(existingOrder.IsArchived), FormatBool(importedOrder.IsArchived));
        AddChange(changes, "XML-Import", FormatBool(existingOrder.IsXmlImported), FormatBool(importedOrder.IsXmlImported));
        AddProductChange(changes, existingOrder.Products, importedOrder.Products);
        AddChange(changes, "Status", Order.NormalizeOrderStatus(existingOrder.OrderStatus), Order.NormalizeOrderStatus(importedOrder.OrderStatus));

        return changes;
    }

    public static bool HasManualLocationAddressChanged(Order existingOrder, Order importedOrder)
    {
        ArgumentNullException.ThrowIfNull(existingOrder);
        ArgumentNullException.ThrowIfNull(importedOrder);

        if (!existingOrder.IsLocationManuallySet)
        {
            return false;
        }

        var confirmedXmlAddress = string.IsNullOrWhiteSpace(existingOrder.ManualLocationAddress)
            ? FormatDeliveryLocationAddress(existingOrder.DeliveryAddress)
            : existingOrder.ManualLocationAddress;
        var importedXmlAddress = FormatDeliveryLocationAddress(importedOrder.DeliveryAddress);
        return !string.Equals(
            OrderAddressFormatting.NormalizeForComparison(
                confirmedXmlAddress,
                OrderAddressComparisonProfile.XmlReimport),
            OrderAddressFormatting.NormalizeForComparison(
                importedXmlAddress,
                OrderAddressComparisonProfile.XmlReimport),
            StringComparison.Ordinal);
    }

    private static void AddChange(
        ICollection<string> changes,
        string label,
        string? oldValue,
        string? newValue)
    {
        var normalizedOld = OrderAddressFormatting.NormalizeForComparison(
            oldValue,
            OrderAddressComparisonProfile.XmlReimport);
        var normalizedNew = OrderAddressFormatting.NormalizeForComparison(
            newValue,
            OrderAddressComparisonProfile.XmlReimport);
        if (string.Equals(normalizedOld, normalizedNew, StringComparison.Ordinal))
        {
            return;
        }

        changes.Add($"{label}: {DisplayValue(normalizedOld)} -> {DisplayValue(normalizedNew)}");
    }

    private static void AddOrderAddressChanges(
        ICollection<string> changes,
        OrderAddressInfo? existingAddress,
        OrderAddressInfo? importedAddress)
    {
        AddChange(changes, "Auftragsadresse Name", existingAddress?.Name, importedAddress?.Name);
        AddChange(changes, "Auftragsadresse Kontaktperson", existingAddress?.ContactPerson, importedAddress?.ContactPerson);
        AddChange(changes, "Auftragsadresse Zusatz", existingAddress?.Additional, importedAddress?.Additional);
        AddChange(changes, "Auftragsadresse Strasse", existingAddress?.Street, importedAddress?.Street);
        AddChange(changes, "Auftragsadresse Hausnummer", existingAddress?.HouseNumber, importedAddress?.HouseNumber);
        AddChange(changes, "Auftragsadresse PLZ", existingAddress?.PostalCode, importedAddress?.PostalCode);
        AddChange(changes, "Auftragsadresse Ort", existingAddress?.City, importedAddress?.City);
    }

    private static void AddDeliveryAddressChanges(
        ICollection<string> changes,
        DeliveryAddressInfo? existingAddress,
        DeliveryAddressInfo? importedAddress)
    {
        AddChange(changes, "Lieferadresse Name", existingAddress?.Name, importedAddress?.Name);
        AddChange(changes, "Lieferadresse Kontaktperson", existingAddress?.ContactPerson, importedAddress?.ContactPerson);
        AddChange(changes, "Lieferadresse Zusatz", existingAddress?.Additional, importedAddress?.Additional);
        AddChange(changes, "Lieferadresse Strasse", existingAddress?.Street, importedAddress?.Street);
        AddChange(changes, "Lieferadresse Hausnummer", existingAddress?.HouseNumber, importedAddress?.HouseNumber);
        AddChange(changes, "Lieferadresse PLZ", existingAddress?.PostalCode, importedAddress?.PostalCode);
        AddChange(changes, "Lieferadresse Ort", existingAddress?.City, importedAddress?.City);
    }

    private static void AddProductChange(
        ICollection<string> changes,
        IReadOnlyList<OrderProductInfo>? existingProducts,
        IReadOnlyList<OrderProductInfo>? importedProducts)
    {
        var existing = existingProducts ?? [];
        var imported = importedProducts ?? [];

        if (existing.Count != imported.Count)
        {
            changes.Add($"Produkte: {existing.Count} -> {imported.Count} Position(en)");
            return;
        }

        for (var index = 0; index < imported.Count; index++)
        {
            var oldLine = FormatProduct(existing[index]);
            var newLine = FormatProduct(imported[index]);
            if (!string.Equals(oldLine, newLine, StringComparison.Ordinal))
            {
                changes.Add($"Produkt {index + 1}: {DisplayValue(oldLine)} -> {DisplayValue(newLine)}");
            }
        }
    }

    private static string FormatDeliveryLocationAddress(DeliveryAddressInfo? address) =>
        address is null
            ? string.Empty
            : OrderAddressFormatting.BuildAddressLine(
                address.Street,
                address.HouseNumber,
                address.PostalCode,
                address.City);

    private static string FormatProduct(OrderProductInfo product) =>
        string.Join(" | ", new[]
        {
            (product.Name ?? string.Empty).Trim(),
            (product.Supplier ?? string.Empty).Trim(),
            $"Menge {product.Quantity}",
            $"Einzelgewicht {product.UnitWeightKg:0.##} kg",
            $"Total {product.WeightKg:0.##} kg",
            OrderProductInfo.NormalizeDeliveryStatus(product.DeliveryStatus)
        });

    private static string FormatOrderType(OrderType type) =>
        type == OrderType.Map ? "Karte" : "Nicht-Karte";

    private static string FormatDate(DateOnly date) =>
        date.ToString("dd.MM.yyyy");

    private static string FormatBool(bool value) =>
        value ? "Ja" : "Nein";

    private static string DisplayValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? "-" : normalized;
    }
}
