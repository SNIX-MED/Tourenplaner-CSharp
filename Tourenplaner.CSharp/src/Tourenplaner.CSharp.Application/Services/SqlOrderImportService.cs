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
    public DateTime ImportedAt { get; set; }
}

public interface ISqlOrderImportService
{
    Task<ImportResult> ImportOrdersAsync(
        List<SqlOrderImportData> sqlOrders,
        IOrderRepository orderRepository,
        ISettingsRepository settingsRepository);

    Task<ImportPreviewResult> PreviewImportAsync(
        List<SqlOrderImportData> sqlOrders,
        IOrderRepository orderRepository);
}

public class SqlOrderImportService : ISqlOrderImportService
{
    public async Task<ImportPreviewResult> PreviewImportAsync(
        List<SqlOrderImportData> sqlOrders,
        IOrderRepository orderRepository)
    {
        var preview = new ImportPreviewResult
        {
            InputOrderCount = sqlOrders?.Count ?? 0
        };

        var existingOrders = (await orderRepository.GetAllAsync()).ToList();
        var existingById = existingOrders
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var sqlOrder in sqlOrders ?? [])
        {
            try
            {
                var previewItem = BuildPreviewItem(sqlOrder, existingById);
                preview.Items.Add(previewItem);

                switch (previewItem.Action)
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
            catch (Exception ex)
            {
                preview.Errors.Add(BuildOrderErrorMessage(sqlOrder.AuftragNr, ex));
            }
        }

        return preview;
    }

    public async Task<ImportResult> ImportOrdersAsync(
        List<SqlOrderImportData> sqlOrders,
        IOrderRepository orderRepository,
        ISettingsRepository settingsRepository)
    {
        var result = new ImportResult { ImportedAt = DateTime.Now };
        var existingOrders = (await orderRepository.GetAllAsync()).ToList();
        var settings = await settingsRepository.GetAsync();

        foreach (var sqlOrder in sqlOrders ?? [])
        {
            try
            {
                var deliveryMethod = DeliveryMethodExtensions.ParseDeliveryMethod(sqlOrder.Lieferbedingung);
                var isMapOrder = deliveryMethod.IsMapOrder();

                var existingOrder = existingOrders
                    .FirstOrDefault(o => string.Equals(o.Id, sqlOrder.AuftragNr, StringComparison.OrdinalIgnoreCase));

                if (existingOrder is null)
                {
                    existingOrders.Add(CreateImportedOrder(sqlOrder, isMapOrder));
                    result.CreatedOrders++;
                    continue;
                }

                var importedOrder = CreateImportedOrder(sqlOrder, isMapOrder, existingOrder);
                var changes = DescribeDifferences(existingOrder, importedOrder);
                if (changes.Count == 0)
                {
                    result.UnchangedOrders++;
                    continue;
                }

                ApplyImportedData(existingOrder, importedOrder);
                result.UpdatedOrders++;
            }
            catch (Exception ex)
            {
                result.Errors.Add(BuildOrderErrorMessage(sqlOrder.AuftragNr, ex));
            }
        }

        if (result.CreatedOrders > 0 || result.UpdatedOrders > 0)
        {
            await orderRepository.SaveAllAsync(existingOrders);
            settings.LastSqlImportDate = DateTime.Now;
            await settingsRepository.SaveAsync(settings);
        }

        return result;
    }

    private ImportPreviewItem BuildPreviewItem(
        SqlOrderImportData sqlOrder,
        IReadOnlyDictionary<string, Order> existingById)
    {
        var deliveryMethod = DeliveryMethodExtensions.ParseDeliveryMethod(sqlOrder.Lieferbedingung);
        var isMapOrder = deliveryMethod.IsMapOrder();
        var normalizedOrderId = (sqlOrder.AuftragNr ?? string.Empty).Trim();

        if (!existingById.TryGetValue(normalizedOrderId, out var existingOrder))
        {
            var newOrder = CreateImportedOrder(sqlOrder, isMapOrder);
            return new ImportPreviewItem
            {
                OrderId = newOrder.Id,
                CustomerName = newOrder.CustomerName,
                Action = ImportPreviewAction.Create,
                DeliveryType = newOrder.DeliveryType,
                OrderTypeLabel = FormatOrderType(newOrder.Type),
                Changes = new List<string>
                {
                    "Neuer Auftrag wird angelegt."
                }
            };
        }

        var importedOrder = CreateImportedOrder(sqlOrder, isMapOrder, existingOrder);
        var changes = DescribeDifferences(existingOrder, importedOrder);
        return new ImportPreviewItem
        {
            OrderId = existingOrder.Id,
            CustomerName = string.IsNullOrWhiteSpace(importedOrder.CustomerName) ? existingOrder.CustomerName : importedOrder.CustomerName,
            Action = changes.Count == 0 ? ImportPreviewAction.Unchanged : ImportPreviewAction.Update,
            DeliveryType = importedOrder.DeliveryType,
            OrderTypeLabel = FormatOrderType(importedOrder.Type),
            Changes = changes
        };
    }

    private Order CreateImportedOrder(
        SqlOrderImportData sqlOrder,
        bool isMapOrder,
        Order? existingOrder = null)
    {
        var deliveryAddress = ResolveDeliveryAddress(sqlOrder);
        var resolvedEmail = ResolvePreferredContact(sqlOrder.LieferEmail, sqlOrder.KundeEmail);
        var resolvedPhone = ResolvePreferredContact(sqlOrder.LieferTelefon, sqlOrder.KundeTelefon);

        var auftragsAdresse = BuildAddress(
            sqlOrder.KundeStrasse,
            sqlOrder.KundeHausnummer,
            sqlOrder.KundePLZ,
            sqlOrder.KundeOrt,
            sqlOrder.KundeLand);
        var orderContactPerson = ResolvePreferredContact(
            sqlOrder.KundeKontaktperson,
            $"{sqlOrder.KundeVorname} {sqlOrder.KundeNachname}");

        var order = new Order
        {
            Id = (sqlOrder.AuftragNr ?? string.Empty).Trim(),
            CustomerName = BuildCustomerName(sqlOrder),
            Address = $"{auftragsAdresse}, {sqlOrder.KundePLZ} {sqlOrder.KundeOrt}".Trim(' ', ','),
            ScheduledDate = DateOnly.FromDateTime(sqlOrder.AuftragsDatum),
            Type = isMapOrder ? OrderType.Map : OrderType.NonMap,
            OrderAddress = new OrderAddressInfo
            {
                Name = sqlOrder.KundeFirma,
                ContactPerson = orderContactPerson,
                Street = auftragsAdresse,
                PostalCode = sqlOrder.KundePLZ,
                City = sqlOrder.KundeOrt
            },
            DeliveryAddress = new DeliveryAddressInfo
            {
                Name = deliveryAddress.Name,
                ContactPerson = deliveryAddress.ContactPerson,
                Street = deliveryAddress.Street,
                PostalCode = deliveryAddress.PostalCode,
                City = deliveryAddress.City
            },
            Email = resolvedEmail,
            Phone = resolvedPhone,
            Products = BuildProducts(sqlOrder.Produkte, existingOrder?.Products),
            DeliveryType = DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(sqlOrder.Lieferbedingung),
            OrderStatus = Order.DefaultOrderStatus,
            Notes = sqlOrder.Notiz,
            IsArchived = sqlOrder.Archiviert
        };

        order.OrderStatus = Order.ResolveOrderStatusFromProducts(order.Products);
        return order;
    }

    private static List<OrderProductInfo> BuildProducts(
        IReadOnlyList<SqlOrderProductData>? sqlProducts,
        IReadOnlyList<OrderProductInfo>? existingProducts)
    {
        var existing = existingProducts ?? [];
        var products = new List<OrderProductInfo>();

        for (var i = 0; i < (sqlProducts ?? []).Count; i++)
        {
            var sqlProduct = sqlProducts![i];
            var previousProduct = i < existing.Count ? existing[i] : null;

            products.Add(new OrderProductInfo
            {
                Name = (sqlProduct.Bezeichnung ?? string.Empty).Trim(),
                Supplier = previousProduct?.Supplier ?? string.Empty,
                Quantity = (int)sqlProduct.Menge,
                UnitWeightKg = (double)sqlProduct.Gewicht,
                WeightKg = (double)(sqlProduct.Gewicht * sqlProduct.Menge),
                Dimensions = previousProduct?.Dimensions ?? string.Empty,
                DeliveryStatus = previousProduct is null
                    ? OrderProductInfo.DefaultDeliveryStatus
                    : OrderProductInfo.NormalizeDeliveryStatus(previousProduct.DeliveryStatus)
            });
        }

        return products;
    }

    private static void ApplyImportedData(Order existingOrder, Order importedOrder)
    {
        existingOrder.CustomerName = importedOrder.CustomerName;
        existingOrder.Address = importedOrder.Address;
        existingOrder.ScheduledDate = importedOrder.ScheduledDate;
        existingOrder.Type = importedOrder.Type;
        existingOrder.OrderAddress = importedOrder.OrderAddress;
        existingOrder.DeliveryAddress = importedOrder.DeliveryAddress;
        existingOrder.Email = importedOrder.Email;
        existingOrder.Phone = importedOrder.Phone;
        existingOrder.Products = importedOrder.Products;
        existingOrder.DeliveryType = importedOrder.DeliveryType;
        existingOrder.OrderStatus = importedOrder.OrderStatus;
        existingOrder.Notes = importedOrder.Notes;
        existingOrder.IsArchived = importedOrder.IsArchived;
    }

    private static List<string> DescribeDifferences(Order existingOrder, Order importedOrder)
    {
        var changes = new List<string>();

        AddChange(changes, "Kunde", existingOrder.CustomerName, importedOrder.CustomerName);
        AddChange(changes, "Termin", FormatDate(existingOrder.ScheduledDate), FormatDate(importedOrder.ScheduledDate));
        AddChange(changes, "Typ", FormatOrderType(existingOrder.Type), FormatOrderType(importedOrder.Type));
        AddChange(changes, "Lieferart", existingOrder.DeliveryType, importedOrder.DeliveryType);
        AddChange(changes, "Auftragsadresse", FormatAddress(existingOrder.OrderAddress), FormatAddress(importedOrder.OrderAddress));
        AddChange(changes, "Lieferadresse", FormatDeliveryAddress(existingOrder.DeliveryAddress), FormatDeliveryAddress(importedOrder.DeliveryAddress));
        AddChange(changes, "E-Mail", existingOrder.Email, importedOrder.Email);
        AddChange(changes, "Telefon", existingOrder.Phone, importedOrder.Phone);
        AddChange(changes, "Notiz", existingOrder.Notes, importedOrder.Notes);
        AddChange(changes, "Archiviert", FormatBool(existingOrder.IsArchived), FormatBool(importedOrder.IsArchived));
        AddProductChange(changes, existingOrder.Products, importedOrder.Products);
        AddChange(changes, "Status", Order.NormalizeOrderStatus(existingOrder.OrderStatus), Order.NormalizeOrderStatus(importedOrder.OrderStatus));

        return changes;
    }

    private static void AddChange(List<string> changes, string label, string? oldValue, string? newValue)
    {
        var normalizedOld = NormalizeComparisonValue(oldValue);
        var normalizedNew = NormalizeComparisonValue(newValue);
        if (string.Equals(normalizedOld, normalizedNew, StringComparison.Ordinal))
        {
            return;
        }

        changes.Add($"{label}: {DisplayValue(normalizedOld)} -> {DisplayValue(normalizedNew)}");
    }

    private static void AddProductChange(
        List<string> changes,
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

        for (var i = 0; i < imported.Count; i++)
        {
            var oldLine = FormatProduct(existing[i]);
            var newLine = FormatProduct(imported[i]);
            if (!string.Equals(oldLine, newLine, StringComparison.Ordinal))
            {
                changes.Add($"Produkt {i + 1}: {DisplayValue(oldLine)} -> {DisplayValue(newLine)}");
            }
        }
    }

    private (string Name, string ContactPerson, string Street, string PostalCode, string City) ResolveDeliveryAddress(SqlOrderImportData sqlOrder)
    {
        var hasSeparateDeliveryAddress =
            !string.IsNullOrWhiteSpace(sqlOrder.LieferStrasse) ||
            !string.IsNullOrWhiteSpace(sqlOrder.LieferHausnummer) ||
            !string.IsNullOrWhiteSpace(sqlOrder.LieferPLZ) ||
            !string.IsNullOrWhiteSpace(sqlOrder.LieferOrt) ||
            !string.IsNullOrWhiteSpace(sqlOrder.LieferLand) ||
            !string.IsNullOrWhiteSpace(sqlOrder.LieferFirma) ||
            !string.IsNullOrWhiteSpace(sqlOrder.LieferVorname) ||
            !string.IsNullOrWhiteSpace(sqlOrder.LieferNachname);

        if (hasSeparateDeliveryAddress)
        {
            var lieferAdresse = BuildAddress(
                sqlOrder.LieferStrasse,
                sqlOrder.LieferHausnummer,
                sqlOrder.LieferPLZ,
                sqlOrder.LieferOrt,
                sqlOrder.LieferLand);

            var name = string.IsNullOrWhiteSpace(sqlOrder.LieferFirma)
                ? BuildCustomerName(sqlOrder)
                : sqlOrder.LieferFirma;
            var contactPerson = ResolvePreferredContact(
                sqlOrder.LieferKontaktperson,
                sqlOrder.KundeKontaktperson);
            if (string.IsNullOrWhiteSpace(contactPerson))
            {
                contactPerson = BuildContactPersonName(sqlOrder);
            }

            return (
                Name: name,
                ContactPerson: contactPerson,
                Street: lieferAdresse,
                PostalCode: sqlOrder.LieferPLZ,
                City: sqlOrder.LieferOrt);
        }

        var auftragsAdresse = BuildAddress(
            sqlOrder.KundeStrasse,
            sqlOrder.KundeHausnummer,
            sqlOrder.KundePLZ,
            sqlOrder.KundeOrt,
            sqlOrder.KundeLand);

        var customerContact = ResolvePreferredContact(sqlOrder.KundeKontaktperson, string.Empty);
        if (string.IsNullOrWhiteSpace(customerContact))
        {
            customerContact = $"{sqlOrder.KundeVorname} {sqlOrder.KundeNachname}".Trim();
        }

        return (
            Name: string.IsNullOrWhiteSpace(sqlOrder.KundeFirma) ? BuildCustomerName(sqlOrder) : sqlOrder.KundeFirma,
            ContactPerson: customerContact,
            Street: auftragsAdresse,
            PostalCode: sqlOrder.KundePLZ,
            City: sqlOrder.KundeOrt);
    }

    private string BuildCustomerName(SqlOrderImportData sqlOrder)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(sqlOrder.KundeFirma))
        {
            parts.Add(sqlOrder.KundeFirma);
        }

        if (!string.IsNullOrWhiteSpace(sqlOrder.KundeNachname))
        {
            parts.Add(sqlOrder.KundeNachname);
        }

        if (!string.IsNullOrWhiteSpace(sqlOrder.KundeVorname))
        {
            parts.Add(sqlOrder.KundeVorname);
        }

        return string.Join(" ", parts).Trim();
    }

    private string BuildContactPersonName(SqlOrderImportData sqlOrder)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(sqlOrder.LieferVorname))
        {
            parts.Add(sqlOrder.LieferVorname);
        }

        if (!string.IsNullOrWhiteSpace(sqlOrder.LieferNachname))
        {
            parts.Add(sqlOrder.LieferNachname);
        }

        if (parts.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(sqlOrder.KundeVorname))
            {
                parts.Add(sqlOrder.KundeVorname);
            }

            if (!string.IsNullOrWhiteSpace(sqlOrder.KundeNachname))
            {
                parts.Add(sqlOrder.KundeNachname);
            }
        }

        return string.Join(" ", parts).Trim();
    }

    private string BuildAddress(string street, string number, string zip, string city, string country)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(street))
        {
            parts.Add(street);
        }

        if (!string.IsNullOrWhiteSpace(number))
        {
            parts.Add(number);
        }

        return string.Join(" ", parts).Trim();
    }

    private static string ResolvePreferredContact(string? deliveryValue, string? orderValue)
    {
        var delivery = (deliveryValue ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(delivery))
        {
            return delivery;
        }

        return (orderValue ?? string.Empty).Trim();
    }

    private static string BuildOrderErrorMessage(string? orderId, Exception ex)
    {
        var normalizedOrderId = (orderId ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalizedOrderId)
            ? $"Fehler bei einem Auftrag ohne AuftragNr: {ex.Message}"
            : $"Fehler bei Auftrag {normalizedOrderId}: {ex.Message}";
    }

    private static string FormatOrderType(OrderType type)
    {
        return type == OrderType.Map ? "Karte" : "Nicht-Karte";
    }

    private static string FormatDate(DateOnly date)
    {
        return date.ToString("dd.MM.yyyy");
    }

    private static string FormatBool(bool value)
    {
        return value ? "Ja" : "Nein";
    }

    private static string FormatAddress(OrderAddressInfo? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        return string.Join(", ", new[]
        {
            (address.Name ?? string.Empty).Trim(),
            (address.ContactPerson ?? string.Empty).Trim(),
            BuildStreetLine(address.Street, address.HouseNumber),
            $"{(address.PostalCode ?? string.Empty).Trim()} {(address.City ?? string.Empty).Trim()}".Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string FormatDeliveryAddress(DeliveryAddressInfo? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        return string.Join(", ", new[]
        {
            (address.Name ?? string.Empty).Trim(),
            (address.ContactPerson ?? string.Empty).Trim(),
            BuildStreetLine(address.Street, address.HouseNumber),
            $"{(address.PostalCode ?? string.Empty).Trim()} {(address.City ?? string.Empty).Trim()}".Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string BuildStreetLine(string? street, string? houseNumber)
    {
        return string.Join(" ", new[]
        {
            (street ?? string.Empty).Trim(),
            (houseNumber ?? string.Empty).Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string FormatProduct(OrderProductInfo product)
    {
        return string.Join(" | ", new[]
        {
            (product.Name ?? string.Empty).Trim(),
            $"Menge {product.Quantity}",
            $"Einzelgewicht {product.UnitWeightKg:0.##} kg",
            $"Total {product.WeightKg:0.##} kg",
            OrderProductInfo.NormalizeDeliveryStatus(product.DeliveryStatus)
        });
    }

    private static string NormalizeComparisonValue(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string DisplayValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized;
    }
}
