using System.Globalization;
using System.Xml.Linq;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Services;

public interface IXmlOrderImportService
{
    List<SqlOrderImportData> LoadOrdersFromFile(string xmlFilePath, XmlImportMappingSettings? mapping = null);
    XmlOrderImportLoadResult LoadOrdersFromFileDetailed(string xmlFilePath, XmlImportMappingSettings? mapping = null);
    string CreateTemplateXml();
}

public sealed class XmlOrderImportService : IXmlOrderImportService
{
    public List<SqlOrderImportData> LoadOrdersFromFile(string xmlFilePath, XmlImportMappingSettings? mapping = null)
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
                var order = new SqlOrderImportData
                {
                    AuftragNr = ReadString(orderElement, effectiveMapping.OrderNumber),
                    Typ = ReadString(orderElement, effectiveMapping.OrderType),
                    AuftragsDatum = ReadDate(orderElement, effectiveMapping.OrderDate, DateTime.Today),
                    Archiviert = ReadBool(orderElement, effectiveMapping.OrderArchived),
                    Gesperrt = ReadBool(orderElement, effectiveMapping.OrderLocked),
                    Lieferbedingung = ReadString(orderElement, effectiveMapping.OrderDeliveryCondition, "Selbstabholung"),
                    Lieferdatum = ReadNullableDate(orderElement, effectiveMapping.OrderDeliveryDate),
                    Notiz = ReadString(orderElement, effectiveMapping.OrderNote)
                };

                var customerAddressId = ReadString(orderElement, effectiveMapping.OrderAddressId);
                if (addressesById.TryGetValue(customerAddressId, out var customerAddressElement))
                {
                    ApplyAddress(order, customerAddressElement, effectiveMapping, isDeliveryAddress: false);
                }

                var deliveryAddressId = ReadString(orderElement, effectiveMapping.OrderDeliveryAddressId);
                if (addressesById.TryGetValue(deliveryAddressId, out var deliveryAddressElement))
                {
                    ApplyAddress(order, deliveryAddressElement, effectiveMapping, isDeliveryAddress: true);
                }

                var orderId = ReadString(orderElement, effectiveMapping.OrderId);
                if (!string.IsNullOrWhiteSpace(orderId) &&
                    productsByOrderId.TryGetValue(orderId, out var matchedProducts))
                {
                    for (var productIndex = 0; productIndex < matchedProducts.Count; productIndex++)
                    {
                        var productElement = matchedProducts[productIndex];
                        order.Produkte.Add(new SqlOrderProductData
                        {
                            PosNummer = productIndex + 1,
                            ArtikelNummer = ReadString(productElement, effectiveMapping.ProductArticleNumber),
                            Bezeichnung = ReadString(productElement, effectiveMapping.ProductDescription),
                            Menge = ReadDecimal(productElement, effectiveMapping.ProductQuantity),
                            Gewicht = ReadDecimal(productElement, effectiveMapping.ProductWeight),
                            Bruttogewicht = 0m
                        });
                    }
                }

                if (string.IsNullOrWhiteSpace(order.AuftragNr))
                {
                    result.Errors.Add($"Auftrag #{index + 1}: AuftragNr fehlt.");
                    continue;
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
            new XElement("NewDataSet",
                new XElement("AVE_Stamm",
                    new XElement("Adresse", "0000101"),
                    new XElement("Firma", "Muster AG"),
                    new XElement("Nachname", "Muster"),
                    new XElement("Vorname", "Max"),
                    new XElement("Strasse", "Musterstrasse 10"),
                    new XElement("PLZ", "8000"),
                    new XElement("Ort", "Zuerich"),
                    new XElement("Land", "CH"),
                    new XElement("Email", "kunde@example.com"),
                    new XElement("Telefon", "+41 44 000 00 00"),
                    new XElement("Kontaktperson", "Max Muster"),
                    new XElement("KontaktEmail", "max.muster@example.com"),
                    new XElement("KontaktTelefon", "+41 44 000 00 11")),
                new XElement("AVE_Stamm",
                    new XElement("Adresse", "0000102"),
                    new XElement("Firma", "Empfaenger GmbH"),
                    new XElement("Nachname", "Empfaenger"),
                    new XElement("Vorname", "Erika"),
                    new XElement("Strasse", "Lieferweg 5"),
                    new XElement("PLZ", "9000"),
                    new XElement("Ort", "St. Gallen"),
                    new XElement("Land", "CH"),
                    new XElement("Email", "lieferung@example.com"),
                    new XElement("Telefon", "+41 71 000 00 00"),
                    new XElement("Kontaktperson", "Erika Empfaenger"),
                    new XElement("KontaktEmail", "erika.empfaenger@example.com"),
                    new XElement("KontaktTelefon", "+41 71 000 00 11")),
                new XElement("WW_Kopf",
                    new XElement("Ident", "6c2752b7-9720-5f72-8445-16b5c8693835"),
                    new XElement("AuftragNr", "A-10001"),
                    new XElement("Typ", "SALES"),
                    new XElement("Datum", "2026-05-28T00:00:00"),
                    new XElement("AdressID", "0000101"),
                    new XElement("LieferadressID", "0000102"),
                    new XElement("LiefKondID", "Lieferung"),
                    new XElement("Lieferdatum", "2026-05-29T00:00:00"),
                    new XElement("Archiviert", "false"),
                    new XElement("Notiz", "Musterdatensatz")),
                new XElement("WW_Pos",
                    new XElement("KopfID", "6c2752b7-9720-5f72-8445-16b5c8693835"),
                    new XElement("PosCode", "ART"),
                    new XElement("ArtikelID", "PRODUKT-A"),
                    new XElement("Menge", "2.000000"),
                    new XElement("Bezeichnung", "Produkt A"),
                    new XElement("Lieferant", "Test-Lieferant"),
                    new XElement("Gewicht", "10.5 kg"))));

        return doc.ToString();
    }

    private static void ApplyAddress(
        SqlOrderImportData order,
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

    private static DateTime ReadDate(XElement parent, string name, DateTime fallback)
        => string.IsNullOrWhiteSpace(name) || !DateTime.TryParse(parent.Element(name)?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
            ? fallback
            : value;

    private static DateTime? ReadNullableDate(XElement parent, string name)
        => string.IsNullOrWhiteSpace(name) || !DateTime.TryParse(parent.Element(name)?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
            ? null
            : value;
}
