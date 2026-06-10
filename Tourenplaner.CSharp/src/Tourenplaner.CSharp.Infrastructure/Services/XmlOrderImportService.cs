using System.Globalization;
using System.Xml.Linq;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Services;

public interface IXmlOrderImportService
{
    List<SqlOrderImportData> LoadOrdersFromFile(string xmlFilePath);
    XmlOrderImportLoadResult LoadOrdersFromFileDetailed(string xmlFilePath);
    string CreateTemplateXml();
}

public sealed class XmlOrderImportService : IXmlOrderImportService
{
    public List<SqlOrderImportData> LoadOrdersFromFile(string xmlFilePath)
    {
        var result = LoadOrdersFromFileDetailed(xmlFilePath);
        if (result.Errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, result.Errors));
        }

        return result.Orders;
    }

    public XmlOrderImportLoadResult LoadOrdersFromFileDetailed(string xmlFilePath)
    {
        if (string.IsNullOrWhiteSpace(xmlFilePath) || !File.Exists(xmlFilePath))
        {
            throw new FileNotFoundException("XML-Datei wurde nicht gefunden.", xmlFilePath);
        }

        var document = XDocument.Load(xmlFilePath);
        var result = new XmlOrderImportLoadResult();

        var orderElements = (document.Root?.Elements("Order") ?? Enumerable.Empty<XElement>()).ToList();
        result.TotalOrderElements = orderElements.Count;
        if (orderElements.Count == 0)
        {
            result.Errors.Add("Keine <Order>-Elemente in der XML-Datei gefunden.");
            return result;
        }

        for (var index = 0; index < orderElements.Count; index++)
        {
            var orderElement = orderElements[index];
            try
            {
                var order = new SqlOrderImportData
                {
                    AuftragNr = ReadString(orderElement, "AuftragNr"),
                    Typ = ReadString(orderElement, "Typ"),
                    AuftragsDatum = ReadDate(orderElement, "AuftragsDatum", DateTime.Today),
                    Archiviert = ReadBool(orderElement, "Archiviert"),
                    Gesperrt = ReadBool(orderElement, "Gesperrt"),
                    KundeFirma = ReadString(orderElement, "KundeFirma"),
                    KundeNachname = ReadString(orderElement, "KundeNachname"),
                    KundeVorname = ReadString(orderElement, "KundeVorname"),
                    KundeStrasse = ReadString(orderElement, "KundeStrasse"),
                    KundeHausnummer = ReadString(orderElement, "KundeHausnummer"),
                    KundePLZ = ReadString(orderElement, "KundePLZ"),
                    KundeOrt = ReadString(orderElement, "KundeOrt"),
                    KundeLand = ReadString(orderElement, "KundeLand"),
                    KundeEmail = ReadString(orderElement, "KundeEmail"),
                    KundeTelefon = ReadString(orderElement, "KundeTelefon"),
                    KundeKontaktperson = ReadString(orderElement, "KundeKontaktperson"),
                    LieferFirma = ReadString(orderElement, "LieferFirma"),
                    LieferNachname = ReadString(orderElement, "LieferNachname"),
                    LieferVorname = ReadString(orderElement, "LieferVorname"),
                    LieferStrasse = ReadString(orderElement, "LieferStrasse"),
                    LieferHausnummer = ReadString(orderElement, "LieferHausnummer"),
                    LieferPLZ = ReadString(orderElement, "LieferPLZ"),
                    LieferOrt = ReadString(orderElement, "LieferOrt"),
                    LieferLand = ReadString(orderElement, "LieferLand"),
                    LieferEmail = ReadString(orderElement, "LieferEmail"),
                    LieferTelefon = ReadString(orderElement, "LieferTelefon"),
                    LieferKontaktperson = ReadString(orderElement, "LieferKontaktperson"),
                    Lieferbedingung = ReadString(orderElement, "Lieferbedingung", "Selbstabholung"),
                    NettoTotal = ReadDecimal(orderElement, "NettoTotal"),
                    BruttoTotal = ReadDecimal(orderElement, "BruttoTotal"),
                    Lieferdatum = ReadNullableDate(orderElement, "Lieferdatum"),
                    Notiz = ReadString(orderElement, "Notiz")
                };

                var productElements = orderElement.Element("Produkte")?.Elements("Produkt") ?? Enumerable.Empty<XElement>();
                foreach (var productElement in productElements)
                {
                    order.Produkte.Add(new SqlOrderProductData
                    {
                        PosNummer = ReadInt(productElement, "PosNummer"),
                        Bezeichnung = ReadString(productElement, "Bezeichnung"),
                        Menge = ReadDecimal(productElement, "Menge"),
                        Gewicht = ReadDecimal(productElement, "Gewicht"),
                        Bruttogewicht = 0m
                    });
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
            new XElement("Orders",
                new XElement("Order",
                    new XElement("AuftragNr", "A-10001"),
                    new XElement("Typ", "Standard"),
                    new XElement("AuftragsDatum", "2026-05-28"),
                    new XElement("Archiviert", "false"),
                    new XElement("Gesperrt", "false"),
                    new XElement("KundeFirma", "Muster AG"),
                    new XElement("KundeNachname", "Muster"),
                    new XElement("KundeVorname", "Max"),
                    new XElement("KundeStrasse", "Musterstrasse"),
                    new XElement("KundeHausnummer", "10"),
                    new XElement("KundePLZ", "8000"),
                    new XElement("KundeOrt", "Zuerich"),
                    new XElement("KundeLand", "CH"),
                    new XElement("KundeEmail", "kunde@example.com"),
                    new XElement("KundeTelefon", "+41 44 000 00 00"),
                    new XElement("KundeKontaktperson", "Max Muster"),
                    new XElement("LieferFirma", "Empfaenger GmbH"),
                    new XElement("LieferNachname", "Empfaenger"),
                    new XElement("LieferVorname", "Erika"),
                    new XElement("LieferStrasse", "Lieferweg"),
                    new XElement("LieferHausnummer", "5"),
                    new XElement("LieferPLZ", "9000"),
                    new XElement("LieferOrt", "St. Gallen"),
                    new XElement("LieferLand", "CH"),
                    new XElement("LieferEmail", "lieferung@example.com"),
                    new XElement("LieferTelefon", "+41 71 000 00 00"),
                    new XElement("LieferKontaktperson", "Erika Empfaenger"),
                    new XElement("Lieferbedingung", "Lieferung"),
                    new XElement("NettoTotal", "100.50"),
                    new XElement("BruttoTotal", "108.25"),
                    new XElement("Lieferdatum", "2026-05-29"),
                    new XElement("Notiz", "Musterdatensatz"),
                    new XElement("Produkte",
                        new XElement("Produkt",
                            new XElement("PosNummer", "1"),
                            new XElement("Bezeichnung", "Produkt A"),
                            new XElement("Menge", "2"),
                            new XElement("Gewicht", "10.5"))))));

        return doc.ToString();
    }

    private static string ReadString(XElement parent, string name, string fallback = "")
        => (parent.Element(name)?.Value ?? fallback).Trim();

    private static bool ReadBool(XElement parent, string name)
        => bool.TryParse(parent.Element(name)?.Value, out var value) && value;

    private static int ReadInt(XElement parent, string name)
        => int.TryParse(parent.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static decimal ReadDecimal(XElement parent, string name)
        => decimal.TryParse(parent.Element(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0m;

    private static DateTime ReadDate(XElement parent, string name, DateTime fallback)
        => DateTime.TryParse(parent.Element(name)?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value) ? value : fallback;

    private static DateTime? ReadNullableDate(XElement parent, string name)
        => DateTime.TryParse(parent.Element(name)?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value) ? value : null;
}
