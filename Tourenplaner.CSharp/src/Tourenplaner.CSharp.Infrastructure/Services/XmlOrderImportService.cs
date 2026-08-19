using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Services;

public interface IXmlOrderImportService
{
    List<XmlOrderImportData> LoadOrdersFromFile(string xmlFilePath, XmlImportMappingSettings? mapping = null);
    XmlOrderImportLoadResult LoadOrdersFromFileDetailed(string xmlFilePath, XmlImportMappingSettings? mapping = null);
    string CreateTemplateXml();
}

public sealed class XmlOrderImportService : IXmlOrderImportService
{
    static XmlOrderImportService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public List<XmlOrderImportData> LoadOrdersFromFile(string xmlFilePath, XmlImportMappingSettings? mapping = null)
    {
        var result = LoadOrdersFromFileDetailed(xmlFilePath, mapping);
        if (result.Errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, result.Errors));
        }

        return result.Orders;
    }

    public XmlOrderImportLoadResult LoadOrdersFromFileDetailed(string xmlFilePath, XmlImportMappingSettings? mapping = null)
    {
        if (string.IsNullOrWhiteSpace(xmlFilePath) || !File.Exists(xmlFilePath))
        {
            throw new FileNotFoundException("XML-Datei wurde nicht gefunden.", xmlFilePath);
        }

        var document = XDocument.Load(xmlFilePath);
        var result = new XmlOrderImportLoadResult();
        var effectiveMapping = (mapping ?? XmlImportMappingSettings.CreateDefault()).WithDefaults();

        if (!document.Descendants(effectiveMapping.OrderRecordElement).Any())
        {
            if (document.Descendants("beleg").Any())
            {
                effectiveMapping = CreateBelegExportMapping(effectiveMapping);
            }
            else if (document.Descendants(XmlImportMappingSettings.LegacyOrderRecordElement).Any())
            {
                effectiveMapping = CreateLegacyMapping(effectiveMapping);
            }
        }

        var addressElements = document.Descendants(effectiveMapping.AddressRecordElement).ToList();
        var orderElements = document.Descendants(effectiveMapping.OrderRecordElement).ToList();
        var productElements = document.Descendants(effectiveMapping.ProductRecordElement).ToList();

        var addressesById = addressElements
            .Select(x => new
            {
                Element = x,
                Id = ReadString(x, effectiveMapping.AddressId)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Element, StringComparer.OrdinalIgnoreCase);

        var productsByOrderId = productElements
            .Select(x => new
            {
                Element = x,
                OrderId = ReadString(x, effectiveMapping.ProductOrderId)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.OrderId))
            .GroupBy(x => x.OrderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Element).ToList(), StringComparer.OrdinalIgnoreCase);

        result.TotalOrderElements = orderElements.Count;
        if (orderElements.Count == 0)
        {
            result.Errors.Add($"Keine <{effectiveMapping.OrderRecordElement}>-Elemente in der XML-Datei gefunden.");
            return result;
        }

        for (var index = 0; index < orderElements.Count; index++)
        {
            var orderElement = orderElements[index];
            try
            {
                var orderNumber = ReadString(orderElement, effectiveMapping.OrderNumber);
                var orderDate = ReadNullableDate(orderElement, effectiveMapping.OrderDate);
                if (!orderDate.HasValue)
                {
                    var orderLabel = string.IsNullOrWhiteSpace(orderNumber) ? $"#{index + 1}" : orderNumber;
                    result.Errors.Add($"Auftrag {orderLabel}: Auftragsdatum im XML-Feld '{effectiveMapping.OrderDate}' fehlt oder ist ungültig.");
                    continue;
                }

                var explicitDeliveryCondition = ReadString(orderElement, effectiveMapping.OrderDeliveryCondition, string.Empty);
                var order = new XmlOrderImportData
                {
                    AuftragNr = orderNumber,
                    Typ = ReadString(orderElement, effectiveMapping.OrderType),
                    AuftragsDatum = orderDate.Value,
                    Archiviert = ReadBool(orderElement, effectiveMapping.OrderArchived),
                    Gesperrt = ReadBool(orderElement, effectiveMapping.OrderLocked),
                    Lieferdatum = ReadNullableDate(orderElement, effectiveMapping.OrderDeliveryDate),
                    LieferungKannFrueherErfolgen = ReadBool(orderElement, effectiveMapping.OrderDeliveryCanOccurEarlier),
                    Lieferzeit = ReadString(orderElement, effectiveMapping.OrderDeliveryTime),
                    IstVorauszahlung = IsPrepaymentCondition(ReadString(orderElement, effectiveMapping.OrderPaymentTerms)),
                    Notiz = ReadString(orderElement, effectiveMapping.OrderNote)
                };

                var customerAddressId = ReadString(orderElement, effectiveMapping.OrderAddressId);
                order.KundeAdressNummer = ResolveAddressNumber(
                    orderElement,
                    effectiveMapping.OrderAddressNumber,
                    customerAddressId);
                if (addressesById.TryGetValue(customerAddressId, out var customerAddressElement))
                {
                    ApplyAddress(order, customerAddressElement, effectiveMapping, isDeliveryAddress: false);
                }

                var hasDeliveryAddress = false;
                var deliveryAddressId = ReadString(orderElement, effectiveMapping.OrderDeliveryAddressId);
                order.LieferAdressNummer = ResolveAddressNumber(
                    orderElement,
                    effectiveMapping.OrderDeliveryAddressNumber,
                    deliveryAddressId);
                if (addressesById.TryGetValue(deliveryAddressId, out var deliveryAddressElement))
                {
                    ApplyAddress(order, deliveryAddressElement, effectiveMapping, isDeliveryAddress: true);
                    hasDeliveryAddress = HasDeliveryAddress(order);
                }
                else if (string.Equals(effectiveMapping.OrderRecordElement, "beleg", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyExportAddressBlock(order, ReadString(orderElement, effectiveMapping.OrderBillingAddressBlock), isDeliveryAddress: false);
                    hasDeliveryAddress = ApplyExportAddressBlock(order, ReadString(orderElement, effectiveMapping.OrderDeliveryAddressBlock), isDeliveryAddress: true);
                }

                var orderId = ReadString(orderElement, effectiveMapping.OrderId);
                if (!string.IsNullOrWhiteSpace(orderId) &&
                    productsByOrderId.TryGetValue(orderId, out var matchedProducts))
                {
                    var deliveryConditionResult = DetermineOrderDeliveryCondition(matchedProducts, explicitDeliveryCondition, effectiveMapping);
                    order.Lieferbedingung = deliveryConditionResult.Label;
                    if (!deliveryConditionResult.WasRecognized)
                    {
                        AddWarning(result, order, index, "keine Lieferart erkannt. Es wird Selbstabholung verwendet.");
                    }

                    var logicalProductIndex = 0;
                    for (var productIndex = 0; productIndex < matchedProducts.Count; productIndex++)
                    {
                        var productElement = matchedProducts[productIndex];
                        if (ShouldSkipProductPosition(productElement, effectiveMapping))
                        {
                            continue;
                        }

                        logicalProductIndex++;
                        order.Produkte.Add(new XmlOrderProductData
                        {
                            PosNummer = logicalProductIndex,
                            ArtikelNummer = ReadString(productElement, effectiveMapping.ProductArticleNumber),
                            Bezeichnung = ReadString(productElement, effectiveMapping.ProductDescription),
                            Menge = ReadDecimal(productElement, effectiveMapping.ProductQuantity),
                            Gewicht = ReadDecimal(productElement, effectiveMapping.ProductWeight),
                            Bruttogewicht = 0m
                        });
                    }
                }
                else
                {
                    order.Lieferbedingung = ResolveExplicitDeliveryCondition(explicitDeliveryCondition);
                    if (string.Equals(order.Lieferbedingung, DeliveryMethodExtensions.SelbstabholungLabel, StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(explicitDeliveryCondition))
                    {
                        AddWarning(result, order, index, "keine Lieferart erkannt. Es wird Selbstabholung verwendet.");
                    }

                    AddWarning(result, order, index, "keine Produktpositionen zum Auftrag gefunden.");
                }

                if (string.IsNullOrWhiteSpace(order.AuftragNr))
                {
                    result.Errors.Add($"Auftrag #{index + 1}: AuftragNr fehlt.");
                    continue;
                }

                if (!hasDeliveryAddress && RequiresDeliveryAddress(order.Lieferbedingung))
                {
                    AddWarning(result, order, index, "keine Lieferadresse gefunden.");
                }

                result.Orders.Add(order);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Auftrag #{index + 1}: {ex.Message}");
            }
        }

        return result;
    }

    public string CreateTemplateXml()
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "Windows-1252", null),
            new XElement("belege",
                new XElement("beleg",
                    new XElement("ident", "6c2752b7-9720-5f72-8445-16b5c8693835"),
                    new XElement("typ", "SALES"),
                    new XElement("kopf", "A-10001"),
                    new XElement("datum", "28.05.2026 00:00:00"),
                    new XElement("adressid", "0000101"),
                    new XElement("firma", "0000101"),
                    new XElement("Standort", "0000202"),
                    new XElement("notiz", "Musterdatensatz"),
                    new XElement("sperre", "False"),
                    new XElement("lieferdatum", "29.05.2026 00:00:00"),
                    new XElement("lieferdatumfrüher", "False"),
                    new XElement("archiv", "False"),
                    new XElement("zahlkondition", "Vorkasse"),
                    new XElement("adresskopfrechnung", "Muster AG\nMusterstrasse 10\n8000 Zuerich"),
                    new XElement("adresskopflieferung", "Empfaenger GmbH\nLieferweg 5\n9000 St. Gallen"),
                    new XElement("versandart", "Post"),
                    new XElement("positionen",
                        new XElement("position",
                            new XElement("ident", "6c2752b7-9720-5f72-8445-16b5c8693836"),
                            new XElement("kopfid", "6c2752b7-9720-5f72-8445-16b5c8693835"),
                            new XElement("menge", "2"),
                            new XElement("bezeichnung", "Produkt A"),
                            new XElement("gewicht", "10.5"),
                            new XElement("artikel", "PRODUKT-A")),
                        new XElement("position",
                            new XElement("ident", "6c2752b7-9720-5f72-8445-16b5c8693837"),
                            new XElement("kopfid", "6c2752b7-9720-5f72-8445-16b5c8693835"),
                            new XElement("menge", "1"),
                            new XElement("bezeichnung", "Frachtposition"),
                            new XElement("gewicht", "0"),
                            new XElement("artikel", "FRACHT-M-VERT"))))));

        return doc.ToString();
    }

    private static void ApplyAddress(
        XmlOrderImportData order,
        XElement addressElement,
        XmlImportMappingSettings mapping,
        bool isDeliveryAddress)
    {
        if (isDeliveryAddress)
        {
            order.LieferFirma = ReadString(addressElement, mapping.AddressCompany);
            order.LieferNachname = ReadString(addressElement, mapping.AddressLastName);
            order.LieferVorname = ReadString(addressElement, mapping.AddressFirstName);
            order.LieferStrasse = ReadString(addressElement, mapping.AddressStreet);
            order.LieferHausnummer = ReadString(addressElement, mapping.AddressHouseNumber);
            order.LieferPLZ = ReadString(addressElement, mapping.AddressPostalCode);
            order.LieferOrt = ReadString(addressElement, mapping.AddressCity);
            order.LieferLand = ReadString(addressElement, mapping.AddressCountry);
            order.LieferEmail = ReadString(addressElement, mapping.AddressEmail);
            order.LieferTelefon = ReadString(addressElement, mapping.AddressPhone);
            order.LieferKontaktperson = ReadString(addressElement, mapping.AddressContactPerson);
            return;
        }

        order.KundeFirma = ReadString(addressElement, mapping.AddressCompany);
        order.KundeNachname = ReadString(addressElement, mapping.AddressLastName);
        order.KundeVorname = ReadString(addressElement, mapping.AddressFirstName);
        order.KundeStrasse = ReadString(addressElement, mapping.AddressStreet);
        order.KundeHausnummer = ReadString(addressElement, mapping.AddressHouseNumber);
        order.KundePLZ = ReadString(addressElement, mapping.AddressPostalCode);
        order.KundeOrt = ReadString(addressElement, mapping.AddressCity);
        order.KundeLand = ReadString(addressElement, mapping.AddressCountry);
        order.KundeEmail = ReadString(addressElement, mapping.AddressEmail);
        order.KundeTelefon = ReadString(addressElement, mapping.AddressPhone);
        order.KundeKontaktperson = ReadString(addressElement, mapping.AddressContactPerson);
    }

    private static XmlImportMappingSettings CreateBelegExportMapping(XmlImportMappingSettings sourceMapping) => new()
    {
        OrderRecordElement = "beleg",
        ProductRecordElement = "position",
        OrderId = "ident",
        OrderNumber = "kopf",
        OrderType = "typ",
        OrderDate = "datum",
        OrderDeliveryCondition = "versandart",
        OrderDeliveryDate = "lieferdatum",
        OrderDeliveryCanOccurEarlier = "lieferdatumfrüher",
        OrderDeliveryTime = sourceMapping.OrderDeliveryTime,
        OrderPaymentTerms = "zahlkondition",
        OrderArchived = "archiv",
        OrderLocked = "sperre",
        OrderNote = "notiz",
        ProductOrderId = "kopfid",
        ProductArticleNumber = "artikel",
        ProductDescription = "bezeichnung",
        ProductQuantity = "menge",
        ProductWeight = "gewicht",
        OrderAddressNumber = sourceMapping.OrderAddressNumber,
        OrderDeliveryAddressNumber = sourceMapping.OrderDeliveryAddressNumber,
        OrderBillingAddressBlock = sourceMapping.OrderBillingAddressBlock,
        OrderDeliveryAddressBlock = sourceMapping.OrderDeliveryAddressBlock,
        ExcludedProductArticleNumbers = sourceMapping.ExcludedProductArticleNumbers,
        ExcludedProductDescriptions = sourceMapping.ExcludedProductDescriptions,
        DeliveryTypeFreiBordsteinkanteArticleNumbers = sourceMapping.DeliveryTypeFreiBordsteinkanteArticleNumbers,
        DeliveryTypeMitVerteilungArticleNumbers = sourceMapping.DeliveryTypeMitVerteilungArticleNumbers,
        DeliveryTypeMitVerteilungMontageArticleNumbers = sourceMapping.DeliveryTypeMitVerteilungMontageArticleNumbers,
        DeliveryTypeSpediteurArticleNumbers = sourceMapping.DeliveryTypeSpediteurArticleNumbers,
        DeliveryTypePostArticleNumbers = sourceMapping.DeliveryTypePostArticleNumbers,
        DeliveryTypeTresorBordsteinArticleNumbers = sourceMapping.DeliveryTypeTresorBordsteinArticleNumbers,
        DeliveryTypeTresorVerwendungArticleNumbers = sourceMapping.DeliveryTypeTresorVerwendungArticleNumbers,
        DeliveryTypeSelbstabholungArticleNumbers = sourceMapping.DeliveryTypeSelbstabholungArticleNumbers
    };

    private static XmlImportMappingSettings CreateLegacyMapping(XmlImportMappingSettings sourceMapping) => new()
    {
        AddressRecordElement = XmlImportMappingSettings.LegacyAddressRecordElement,
        OrderRecordElement = XmlImportMappingSettings.LegacyOrderRecordElement,
        ProductRecordElement = XmlImportMappingSettings.LegacyProductRecordElement,
        AddressId = XmlImportMappingSettings.LegacyAddressId,
        AddressCompany = XmlImportMappingSettings.LegacyAddressCompany,
        AddressLastName = XmlImportMappingSettings.LegacyAddressLastName,
        AddressFirstName = XmlImportMappingSettings.LegacyAddressFirstName,
        AddressStreet = XmlImportMappingSettings.LegacyAddressStreet,
        AddressHouseNumber = XmlImportMappingSettings.LegacyAddressHouseNumber,
        AddressPostalCode = XmlImportMappingSettings.LegacyAddressPostalCode,
        AddressCity = XmlImportMappingSettings.LegacyAddressCity,
        AddressCountry = XmlImportMappingSettings.LegacyAddressCountry,
        AddressEmail = XmlImportMappingSettings.LegacyAddressEmail,
        AddressPhone = XmlImportMappingSettings.LegacyAddressPhone,
        AddressContactPerson = XmlImportMappingSettings.LegacyAddressContactPerson,
        OrderId = XmlImportMappingSettings.LegacyOrderId,
        OrderNumber = XmlImportMappingSettings.LegacyOrderNumber,
        OrderType = XmlImportMappingSettings.LegacyOrderType,
        OrderDate = XmlImportMappingSettings.LegacyOrderDate,
        OrderAddressId = XmlImportMappingSettings.LegacyOrderAddressId,
        OrderDeliveryAddressId = XmlImportMappingSettings.LegacyOrderDeliveryAddressId,
        OrderAddressNumber = sourceMapping.OrderAddressNumber,
        OrderDeliveryAddressNumber = sourceMapping.OrderDeliveryAddressNumber,
        OrderDeliveryCondition = XmlImportMappingSettings.LegacyOrderDeliveryCondition,
        OrderDeliveryDate = XmlImportMappingSettings.LegacyOrderDeliveryDate,
        OrderDeliveryCanOccurEarlier = XmlImportMappingSettings.LegacyOrderDeliveryCanOccurEarlier,
        OrderDeliveryTime = sourceMapping.OrderDeliveryTime,
        OrderPaymentTerms = sourceMapping.OrderPaymentTerms,
        OrderArchived = XmlImportMappingSettings.LegacyOrderArchived,
        OrderLocked = XmlImportMappingSettings.LegacyOrderLocked,
        OrderNote = XmlImportMappingSettings.LegacyOrderNote,
        ProductOrderId = XmlImportMappingSettings.LegacyProductOrderId,
        ProductArticleNumber = XmlImportMappingSettings.LegacyProductArticleNumber,
        ProductDescription = XmlImportMappingSettings.LegacyProductDescription,
        ProductQuantity = XmlImportMappingSettings.LegacyProductQuantity,
        ProductWeight = XmlImportMappingSettings.LegacyProductWeight,
        ExcludedProductArticleNumbers = sourceMapping.ExcludedProductArticleNumbers,
        ExcludedProductDescriptions = sourceMapping.ExcludedProductDescriptions,
        DeliveryTypeFreiBordsteinkanteArticleNumbers = sourceMapping.DeliveryTypeFreiBordsteinkanteArticleNumbers,
        DeliveryTypeMitVerteilungArticleNumbers = sourceMapping.DeliveryTypeMitVerteilungArticleNumbers,
        DeliveryTypeMitVerteilungMontageArticleNumbers = sourceMapping.DeliveryTypeMitVerteilungMontageArticleNumbers,
        DeliveryTypeSpediteurArticleNumbers = sourceMapping.DeliveryTypeSpediteurArticleNumbers,
        DeliveryTypePostArticleNumbers = sourceMapping.DeliveryTypePostArticleNumbers,
        DeliveryTypeTresorBordsteinArticleNumbers = sourceMapping.DeliveryTypeTresorBordsteinArticleNumbers,
        DeliveryTypeTresorVerwendungArticleNumbers = sourceMapping.DeliveryTypeTresorVerwendungArticleNumbers,
        DeliveryTypeSelbstabholungArticleNumbers = sourceMapping.DeliveryTypeSelbstabholungArticleNumbers
    };

    private static DeliveryConditionResult DetermineOrderDeliveryCondition(
        IReadOnlyList<XElement> matchedProducts,
        string explicitDeliveryCondition,
        XmlImportMappingSettings mapping)
    {
        var productDeliveryCondition = ParseDeliveryConditionFromProductArticleNumbers(matchedProducts, mapping);
        if (!string.IsNullOrWhiteSpace(productDeliveryCondition))
        {
            return new DeliveryConditionResult(productDeliveryCondition, true);
        }

        if (!string.IsNullOrWhiteSpace(explicitDeliveryCondition))
        {
            return new DeliveryConditionResult(explicitDeliveryCondition.Trim(), true);
        }

        return new DeliveryConditionResult(DeliveryMethodExtensions.SelbstabholungLabel, false);
    }

    private static string ResolveExplicitDeliveryCondition(string explicitDeliveryCondition)
    {
        return !string.IsNullOrWhiteSpace(explicitDeliveryCondition)
            ? explicitDeliveryCondition.Trim()
            : DeliveryMethodExtensions.SelbstabholungLabel;
    }

    private static bool IsPrepaymentCondition(string? paymentTerms)
    {
        var normalized = (paymentTerms ?? string.Empty).Trim();
        return normalized.Equals("Vorkasse", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Vorauskasse", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Vorauszahlung", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveAddressNumber(XElement orderElement, string fieldName, string fallback)
    {
        var explicitValue = ReadString(orderElement, fieldName, string.Empty);
        return ExtractTrailingAddressNumber(!string.IsNullOrWhiteSpace(explicitValue)
            ? explicitValue
            : fallback);
    }

    private static string ExtractTrailingAddressNumber(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var segments = normalized
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0
            ? normalized
            : segments[^1].Trim();
    }

    private static string? ParseDeliveryConditionFromProductArticleNumbers(
        IReadOnlyList<XElement> matchedProducts,
        XmlImportMappingSettings mapping)
    {
        var rules = GetDeliveryTypeRules(mapping);
        if (rules.Count == 0)
        {
            return null;
        }

        var matchedRules = new List<DeliveryTypeRule>();
        foreach (var productElement in matchedProducts)
        {
            var productArticleNumber = GetDeliveryTypeMatchArticleNumber(productElement, mapping);
            if (string.IsNullOrWhiteSpace(productArticleNumber))
            {
                continue;
            }

            foreach (var rule in rules)
            {
                if (rule.MatchValues.Contains(productArticleNumber))
                {
                    matchedRules.Add(rule);
                }
            }
        }

        if (matchedRules.Count == 0)
        {
            return null;
        }

        return matchedRules
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .First().Label;
    }

    private static string GetDeliveryTypeMatchArticleNumber(XElement productElement, XmlImportMappingSettings mapping)
    {
        return NormalizeDeliveryTypeArticleNumber(ReadString(productElement, mapping.ProductArticleNumber));
    }

    private static IReadOnlyList<DeliveryTypeRule> GetDeliveryTypeRules(XmlImportMappingSettings mapping)
    {
        return new List<DeliveryTypeRule>
        {
            new(DeliveryMethodExtensions.MitVerteilungMontage, CreateDeliveryTypeMatchValues(mapping.DeliveryTypeMitVerteilungMontageArticleNumbers, DeliveryMethodExtensions.MitVerteilungMontage), 900),
            new(DeliveryMethodExtensions.MitVerteilung, CreateDeliveryTypeMatchValues(mapping.DeliveryTypeMitVerteilungArticleNumbers, DeliveryMethodExtensions.MitVerteilung), 500),
            new(DeliveryMethodExtensions.FreiBordsteinkante, CreateDeliveryTypeMatchValues(mapping.DeliveryTypeFreiBordsteinkanteArticleNumbers, DeliveryMethodExtensions.FreiBordsteinkante), 400),
            new(DeliveryMethodExtensions.Spediteur, CreateDeliveryTypeMatchValues(mapping.DeliveryTypeSpediteurArticleNumbers, DeliveryMethodExtensions.Spediteur), 700),
            new(DeliveryMethodExtensions.Post, CreateDeliveryTypeMatchValues(mapping.DeliveryTypePostArticleNumbers, DeliveryMethodExtensions.Post), 700),
            new(DeliveryMethodExtensions.TresorBordstein, CreateDeliveryTypeMatchValues(mapping.DeliveryTypeTresorBordsteinArticleNumbers, DeliveryMethodExtensions.TresorBordstein), 700),
            new(DeliveryMethodExtensions.TresorVerwendung, CreateDeliveryTypeMatchValues(mapping.DeliveryTypeTresorVerwendungArticleNumbers, DeliveryMethodExtensions.TresorVerwendung), 700),
            new(DeliveryMethodExtensions.SelbstabholungLabel, CreateDeliveryTypeMatchValues(mapping.DeliveryTypeSelbstabholungArticleNumbers, DeliveryMethodExtensions.SelbstabholungLabel), 300)
        };
    }

    private static IReadOnlySet<string> CreateDeliveryTypeMatchValues(string? articleNumbers, string deliveryTypeLabel)
    {
        var values = new HashSet<string>(ParseDelimitedDeliveryTypeArticleNumbers(articleNumbers), StringComparer.OrdinalIgnoreCase)
        {
            NormalizeDeliveryTypeArticleNumber(deliveryTypeLabel)
        };

        return values;
    }

    private static IReadOnlySet<string> ParseDelimitedDeliveryTypeArticleNumbers(string? articleNumbers)
    {
        return (articleNumbers ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeDeliveryTypeArticleNumber)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDeliveryTypeMarker(XElement productElement, XmlImportMappingSettings mapping)
    {
        var productArticleNumber = GetDeliveryTypeMatchArticleNumber(productElement, mapping);
        return !string.IsNullOrWhiteSpace(productArticleNumber) &&
               GetDeliveryTypeRules(mapping).Any(rule => rule.MatchValues.Contains(productArticleNumber));
    }

    private static bool ShouldSkipProductPosition(XElement productElement, XmlImportMappingSettings mapping)
    {
        if (IsDeliveryTypeMarker(productElement, mapping))
        {
            return true;
        }

        var articleNumber = NormalizeProductExclusionValue(ReadString(productElement, mapping.ProductArticleNumber));
        if (!string.IsNullOrWhiteSpace(articleNumber) &&
            ParseDelimitedProductExclusionValues(mapping.ExcludedProductArticleNumbers).Contains(articleNumber))
        {
            return true;
        }

        var description = NormalizeProductExclusionValue(ReadString(productElement, mapping.ProductDescription));
        return !string.IsNullOrWhiteSpace(description) &&
               ParseDelimitedProductExclusionValues(mapping.ExcludedProductDescriptions)
                   .Any(pattern => description.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeDeliveryTypeArticleNumber(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static IReadOnlySet<string> ParseDelimitedProductExclusionValues(string? values)
    {
        return (values ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeProductExclusionValue)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeProductExclusionValue(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private sealed record DeliveryConditionResult(string Label, bool WasRecognized);

    private sealed record DeliveryTypeRule(string Label, IReadOnlySet<string> MatchValues, int Priority);

    private static bool ApplyExportAddressBlock(XmlOrderImportData order, string value, bool isDeliveryAddress)
    {
        var lines = (value ?? string.Empty).Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return false;
        var name = string.Join(' ', lines.Take(Math.Max(1, lines.Length - 2)));
        var street = lines.Length >= 2 ? lines[^2] : string.Empty;
        var postalCity = lines.Length >= 1 ? lines[^1] : string.Empty;
        var postalParts = postalCity.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (isDeliveryAddress)
        {
            order.LieferFirma = name; order.LieferStrasse = street; order.LieferPLZ = postalParts.ElementAtOrDefault(0) ?? string.Empty; order.LieferOrt = postalParts.ElementAtOrDefault(1) ?? string.Empty;
        }
        else
        {
            order.KundeFirma = name; order.KundeStrasse = street; order.KundePLZ = postalParts.ElementAtOrDefault(0) ?? string.Empty; order.KundeOrt = postalParts.ElementAtOrDefault(1) ?? string.Empty;
        }

        return isDeliveryAddress ? HasDeliveryAddress(order) : HasCustomerAddress(order);
    }

    private static bool HasDeliveryAddress(XmlOrderImportData order)
    {
        return HasAnyValue(
            order.LieferFirma,
            order.LieferNachname,
            order.LieferVorname,
            order.LieferStrasse,
            order.LieferPLZ,
            order.LieferOrt);
    }

    private static bool RequiresDeliveryAddress(string? deliveryCondition)
    {
        return !string.Equals(
            DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(deliveryCondition),
            DeliveryMethodExtensions.SelbstabholungLabel,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCustomerAddress(XmlOrderImportData order)
    {
        return HasAnyValue(
            order.KundeFirma,
            order.KundeNachname,
            order.KundeVorname,
            order.KundeStrasse,
            order.KundePLZ,
            order.KundeOrt);
    }

    private static bool HasAnyValue(params string?[] values)
    {
        return values.Any(x => !string.IsNullOrWhiteSpace(x));
    }

    private static void AddWarning(XmlOrderImportLoadResult result, XmlOrderImportData order, int orderIndex, string message)
    {
        var orderLabel = !string.IsNullOrWhiteSpace(order.AuftragNr)
            ? order.AuftragNr.Trim()
            : $"#{orderIndex + 1}";

        result.Warnings.Add($"Auftrag {orderLabel}: {message}");
    }

    private static string ReadString(XElement parent, string name, string fallback = "")
        => string.IsNullOrWhiteSpace(name) ? fallback.Trim() : (parent.Element(name)?.Value ?? fallback).Trim();

    private static bool ReadBool(XElement parent, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var raw = (parent.Element(name)?.Value ?? string.Empty).Trim();
        if (bool.TryParse(raw, out var boolValue))
        {
            return boolValue;
        }

        return raw switch
        {
            "1" => true,
            "0" => false,
            _ => false
        };
    }

    private static int ReadInt(XElement parent, string name)
        => int.TryParse(parent.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static decimal ReadDecimal(XElement parent, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0m;
        }

        var raw = (parent.Element(name)?.Value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0m;
        }

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var directValue))
        {
            return directValue;
        }

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.GetCultureInfo("de-CH"), out var localValue))
        {
            return localValue;
        }

        var sanitized = new string(raw
            .Where(ch => char.IsDigit(ch) || ch is '.' or ',' or '-' or '+')
            .ToArray());

        if (decimal.TryParse(sanitized.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var sanitizedValue))
        {
            return sanitizedValue;
        }

        return 0m;
    }

    private static DateTime? ReadNullableDate(XElement parent, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var raw = (parent.Element(name)?.Value ?? string.Empty).Trim();
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value) ||
            DateTime.TryParse(raw, CultureInfo.GetCultureInfo("de-CH"), DateTimeStyles.AssumeLocal, out value) ||
            DateTime.TryParse(raw, CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.AssumeLocal, out value))
        {
            return value.Date <= DateTime.MinValue.Date ? null : value;
        }

        return null;
    }
}
