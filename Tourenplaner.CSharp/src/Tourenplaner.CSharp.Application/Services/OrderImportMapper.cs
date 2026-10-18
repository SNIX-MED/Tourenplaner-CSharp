using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class OrderImportMapper
{
    private readonly Func<string> _createPositionId;

    public OrderImportMapper(Func<string>? createPositionId = null)
    {
        _createPositionId = createPositionId ?? (() => Guid.NewGuid().ToString("N"));
    }

    public Order Map(
        XmlOrderImportData xmlOrder,
        Order? existingOrder = null,
        bool markAsXmlImported = false)
    {
        ArgumentNullException.ThrowIfNull(xmlOrder);

        var deliveryAddress = ResolveDeliveryAddress(xmlOrder);
        var resolvedEmail = ResolvePreferredContact(xmlOrder.LieferEmail, xmlOrder.KundeEmail);
        var resolvedPhone = ResolvePreferredContact(xmlOrder.LieferTelefon, xmlOrder.KundeTelefon);
        var customerName = BuildCustomerName(xmlOrder);
        var orderStreetLine = OrderAddressFormatting.BuildStreetLine(
            xmlOrder.KundeStrasse,
            xmlOrder.KundeHausnummer);
        var orderContactPerson = ResolvePreferredContact(
            xmlOrder.KundeKontaktperson,
            $"{xmlOrder.KundeVorname} {xmlOrder.KundeNachname}");
        var deliveryMethod = DeliveryMethodExtensions.ParseDeliveryMethod(xmlOrder.Lieferbedingung);
        var normalizedDeliveryType = DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(xmlOrder.Lieferbedingung);

        var order = new Order
        {
            Id = (xmlOrder.AuftragNr ?? string.Empty).Trim(),
            CustomerName = customerName,
            Address = $"{orderStreetLine}, {xmlOrder.KundePLZ} {xmlOrder.KundeOrt}".Trim(' ', ','),
            ScheduledDate = DateOnly.FromDateTime(xmlOrder.AuftragsDatum),
            DeliveryDate = NormalizeDeliveryDate(xmlOrder.Lieferdatum),
            DeliveryPeriod = (xmlOrder.Lieferdatum2 ?? string.Empty).Trim(),
            DeliveryCanOccurEarlier = xmlOrder.LieferungKannFrueherErfolgen,
            Type = deliveryMethod.IsMapOrder() ? OrderType.Map : OrderType.NonMap,
            OrderAddress = new OrderAddressInfo
            {
                Name = xmlOrder.KundeFirma,
                ContactPerson = DistinctContactPerson(customerName, orderContactPerson),
                Street = (xmlOrder.KundeStrasse ?? string.Empty).Trim(),
                HouseNumber = (xmlOrder.KundeHausnummer ?? string.Empty).Trim(),
                PostalCode = xmlOrder.KundePLZ,
                City = xmlOrder.KundeOrt
            },
            DeliveryAddress = new DeliveryAddressInfo
            {
                Name = deliveryAddress.Name,
                ContactPerson = deliveryAddress.ContactPerson,
                Street = deliveryAddress.Street,
                HouseNumber = deliveryAddress.HouseNumber,
                PostalCode = deliveryAddress.PostalCode,
                City = deliveryAddress.City
            },
            Email = resolvedEmail,
            Phone = resolvedPhone,
            Products = BuildProducts(xmlOrder.Produkte, existingOrder?.Products, xmlOrder.Lieferzeit),
            ManualTotalWeightKg = existingOrder?.ManualTotalWeightKg,
            ManualTotalWeightInfo = existingOrder?.ManualTotalWeightInfo ?? string.Empty,
            DeliveryType = normalizedDeliveryType,
            IsAlternativeDeliveryEnabled = existingOrder?.IsAlternativeDeliveryEnabled == true &&
                                           string.Equals(
                                               DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(existingOrder.DeliveryType),
                                               normalizedDeliveryType,
                                               StringComparison.OrdinalIgnoreCase) &&
                                           DeliveryMethodExtensions.SupportsAlternativeDelivery(xmlOrder.Lieferbedingung),
            IsSelfPickupAlternativeEnabled = existingOrder?.IsSelfPickupAlternativeEnabled == true &&
                                             !string.Equals(
                                                 normalizedDeliveryType,
                                                 DeliveryMethodExtensions.SelbstabholungLabel,
                                                 StringComparison.OrdinalIgnoreCase),
            OrderStatus = Order.DefaultOrderStatus,
            Notes = (xmlOrder.Notiz ?? string.Empty).Trim(),
            IstVorauszahlung = xmlOrder.IstVorauszahlung,
            IstVorauszahlungBezahlt = xmlOrder.IstVorauszahlungBezahlt,
            IsArchived = xmlOrder.Archiviert,
            IsXmlImported = markAsXmlImported || existingOrder?.IsXmlImported == true
        };

        order.OrderStatus = Order.ResolveOrderStatusFromProducts(order.Products);
        return order;
    }

    private List<OrderProductInfo> BuildProducts(
        IReadOnlyList<XmlOrderProductData>? xmlProducts,
        IReadOnlyList<OrderProductInfo>? existingProducts,
        string? orderDeliveryTime)
    {
        var importedProducts = xmlProducts ?? [];
        var existing = existingProducts ?? [];
        var products = new List<OrderProductInfo>(importedProducts.Count);

        for (var index = 0; index < importedProducts.Count; index++)
        {
            var xmlProduct = importedProducts[index];
            var previousProduct = index < existing.Count ? existing[index] : null;
            var quantity = (int)xmlProduct.Menge;
            var unitWeightKg = (double)xmlProduct.Gewicht;

            products.Add(new OrderProductInfo
            {
                PositionId = previousProduct?.PositionId ?? _createPositionId(),
                ExternalPositionId = previousProduct?.ExternalPositionId ?? string.Empty,
                Name = (xmlProduct.Bezeichnung ?? string.Empty).Trim(),
                Supplier = !string.IsNullOrWhiteSpace(xmlProduct.Lieferant)
                    ? xmlProduct.Lieferant.Trim()
                    : previousProduct?.Supplier ?? string.Empty,
                ExpectedDelivery = previousProduct?.ExpectedDelivery ?? string.Empty,
                Quantity = quantity,
                UnitWeightKg = unitWeightKg,
                WeightKg = unitWeightKg * quantity,
                Dimensions = previousProduct?.Dimensions ?? string.Empty,
                DeliveryStatus = previousProduct is null
                    ? ResolveImportedProductDeliveryStatus(
                        xmlProduct.Lieferzeit,
                        orderDeliveryTime,
                        xmlProduct.Lieferant)
                    : OrderProductInfo.NormalizeDeliveryStatus(previousProduct.DeliveryStatus)
            });
        }

        return products;
    }

    private static string ResolveImportedProductDeliveryStatus(
        string? productDeliveryTime,
        string? orderDeliveryTime,
        string? supplier)
    {
        var deliveryTime = !string.IsNullOrWhiteSpace(productDeliveryTime)
            ? productDeliveryTime
            : orderDeliveryTime;
        if (!(deliveryTime ?? string.Empty).Trim().StartsWith("ab Lager", StringComparison.OrdinalIgnoreCase))
        {
            return OrderProductInfo.OrderedStatus;
        }

        return (supplier ?? string.Empty).Trim().StartsWith("Esnova Racks S.A.", StringComparison.OrdinalIgnoreCase)
            ? OrderProductInfo.PendingPreparationStatus
            : OrderProductInfo.InStockStatus;
    }

    private static DeliveryAddressComponents ResolveDeliveryAddress(XmlOrderImportData xmlOrder)
    {
        var hasSeparateDeliveryAddress =
            !string.IsNullOrWhiteSpace(xmlOrder.LieferStrasse) ||
            !string.IsNullOrWhiteSpace(xmlOrder.LieferHausnummer) ||
            !string.IsNullOrWhiteSpace(xmlOrder.LieferPLZ) ||
            !string.IsNullOrWhiteSpace(xmlOrder.LieferOrt) ||
            !string.IsNullOrWhiteSpace(xmlOrder.LieferLand) ||
            !string.IsNullOrWhiteSpace(xmlOrder.LieferFirma) ||
            !string.IsNullOrWhiteSpace(xmlOrder.LieferVorname) ||
            !string.IsNullOrWhiteSpace(xmlOrder.LieferNachname);

        if (hasSeparateDeliveryAddress)
        {
            var name = string.IsNullOrWhiteSpace(xmlOrder.LieferFirma)
                ? BuildCustomerName(xmlOrder)
                : xmlOrder.LieferFirma;
            var contactPerson = ResolvePreferredContact(
                xmlOrder.LieferKontaktperson,
                xmlOrder.KundeKontaktperson);
            if (string.IsNullOrWhiteSpace(contactPerson))
            {
                contactPerson = BuildContactPersonName(xmlOrder);
            }

            return new DeliveryAddressComponents(
                name,
                DistinctContactPerson(name, contactPerson),
                (xmlOrder.LieferStrasse ?? string.Empty).Trim(),
                (xmlOrder.LieferHausnummer ?? string.Empty).Trim(),
                xmlOrder.LieferPLZ,
                xmlOrder.LieferOrt);
        }

        var customerName = BuildCustomerName(xmlOrder);
        var customerAddressName = string.IsNullOrWhiteSpace(xmlOrder.KundeFirma)
            ? customerName
            : xmlOrder.KundeFirma;
        var customerContact = ResolvePreferredContact(xmlOrder.KundeKontaktperson, string.Empty);
        if (string.IsNullOrWhiteSpace(customerContact))
        {
            customerContact = $"{xmlOrder.KundeVorname} {xmlOrder.KundeNachname}".Trim();
        }

        return new DeliveryAddressComponents(
            customerAddressName,
            DistinctContactPerson(customerAddressName, customerContact),
            (xmlOrder.KundeStrasse ?? string.Empty).Trim(),
            (xmlOrder.KundeHausnummer ?? string.Empty).Trim(),
            xmlOrder.KundePLZ,
            xmlOrder.KundeOrt);
    }

    private static string BuildCustomerName(XmlOrderImportData xmlOrder) =>
        string.Join(" ", new[]
        {
            xmlOrder.KundeFirma,
            xmlOrder.KundeNachname,
            xmlOrder.KundeVorname
        }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();

    private static string BuildContactPersonName(XmlOrderImportData xmlOrder)
    {
        var deliveryContact = string.Join(" ", new[]
        {
            xmlOrder.LieferVorname,
            xmlOrder.LieferNachname
        }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();

        return deliveryContact.Length > 0
            ? deliveryContact
            : string.Join(" ", new[]
            {
                xmlOrder.KundeVorname,
                xmlOrder.KundeNachname
            }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
    }

    private static string ResolvePreferredContact(string? deliveryValue, string? orderValue)
    {
        var delivery = (deliveryValue ?? string.Empty).Trim();
        return delivery.Length > 0 ? delivery : (orderValue ?? string.Empty).Trim();
    }

    private static string DistinctContactPerson(string name, string contactPerson) =>
        string.Equals(name.Trim(), contactPerson.Trim(), StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : contactPerson.Trim();

    private static DateOnly? NormalizeDeliveryDate(DateTime? deliveryDate) =>
        !deliveryDate.HasValue || deliveryDate.Value.Date <= DateTime.MinValue.Date
            ? null
            : DateOnly.FromDateTime(deliveryDate.Value);

    private sealed record DeliveryAddressComponents(
        string Name,
        string ContactPerson,
        string Street,
        string HouseNumber,
        string PostalCode,
        string City);
}
