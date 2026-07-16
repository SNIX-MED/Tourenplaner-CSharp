using Tourenplaner.CSharp.Infrastructure.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public class XmlOrderImportServiceTests
{
    [Fact]
    public void LoadOrdersFromFileDetailed_UsesDefaultTemplateMapping()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "orders.xml");

        try
        {
            File.WriteAllText(xmlPath,
                """
                <NewDataSet>
                  <AVE_Stamm>
                    <Adresse>100</Adresse>
                    <Firma>Muster AG</Firma>
                    <Nachname>Muster</Nachname>
                    <Vorname>Max</Vorname>
                    <Strasse>Musterstrasse 10</Strasse>
                    <PLZ>8000</PLZ>
                    <Ort>Zuerich</Ort>
                    <Land>CH</Land>
                    <Email>kunde@example.com</Email>
                    <Telefon>+41 44 000 00 00</Telefon>
                    <Kontaktperson>Max Muster</Kontaktperson>
                  </AVE_Stamm>
                  <AVE_Stamm>
                    <Adresse>200</Adresse>
                    <Firma>Empfaenger GmbH</Firma>
                    <Nachname>Empfaenger</Nachname>
                    <Vorname>Erika</Vorname>
                    <Strasse>Lieferweg 5</Strasse>
                    <PLZ>9000</PLZ>
                    <Ort>St. Gallen</Ort>
                    <Land>CH</Land>
                    <Email>lieferung@example.com</Email>
                    <Telefon>+41 71 000 00 00</Telefon>
                    <Kontaktperson>Erika Empfaenger</Kontaktperson>
                  </AVE_Stamm>
                  <WW_Kopf>
                    <Ident>order-1</Ident>
                    <AuftragNr>A-200</AuftragNr>
                    <Typ>SALES</Typ>
                    <Datum>2026-06-10T00:00:00</Datum>
                    <AdressID>100</AdressID>
                    <LieferadressID>200</LieferadressID>
                    <LiefKondID>Lieferung</LiefKondID>
                    <Lieferdatum>2026-06-11T00:00:00</Lieferdatum>
                    <Archiviert>false</Archiviert>
                    <Notiz>Testnotiz</Notiz>
                  </WW_Kopf>
                  <WW_Pos>
                    <KopfID>order-1</KopfID>
                    <ArtikelID>PRODUKT-A</ArtikelID>
                    <Bezeichnung>Produkt A</Bezeichnung>
                    <Menge>2.000000</Menge>
                    <Gewicht>10.5 kg</Gewicht>
                  </WW_Pos>
                </NewDataSet>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Equal(1, result.TotalOrderElements);
            Assert.Single(result.Orders);
            Assert.Equal("A-200", result.Orders[0].AuftragNr);
            Assert.Equal("Muster AG", result.Orders[0].KundeFirma);
            Assert.Equal("Empfaenger GmbH", result.Orders[0].LieferFirma);
            Assert.Single(result.Orders[0].Produkte);
            Assert.Equal("PRODUKT-A", result.Orders[0].Produkte[0].ArtikelNummer);
            Assert.Equal(10.5m, result.Orders[0].Produkte[0].Gewicht);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_UsesCustomMappingAndFallsBackToDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "orders.xml");

        try
        {
            File.WriteAllText(xmlPath,
                """
                <NewDataSet>
                  <AddrRow>
                    <Adresse>100</Adresse>
                    <Company>Muster AG</Company>
                    <Nachname>Muster</Nachname>
                    <Vorname>Max</Vorname>
                    <Street>Musterstrasse 10</Street>
                    <PLZ>8000</PLZ>
                    <Ort>Zuerich</Ort>
                    <Land>CH</Land>
                    <Email>kunde@example.com</Email>
                    <Telefon>+41 44 000 00 00</Telefon>
                    <Kontaktperson>Max Muster</Kontaktperson>
                  </AddrRow>
                  <OrderRow>
                    <Ident>order-1</Ident>
                    <OrderNo>A-201</OrderNo>
                    <Typ>SALES</Typ>
                    <OrderDate>2026-06-10T00:00:00</OrderDate>
                    <AddressRef>100</AddressRef>
                    <LiefKondID>Lieferung</LiefKondID>
                    <Archiviert>false</Archiviert>
                    <Notiz>Testnotiz</Notiz>
                  </OrderRow>
                  <PositionRow>
                    <KopfID>order-1</KopfID>
                    <ItemNo>ART-77</ItemNo>
                    <ItemName>Produkt B</ItemName>
                    <Qty>3</Qty>
                    <WeightKg>7.25</WeightKg>
                  </PositionRow>
                </NewDataSet>
                """);

            var service = new XmlOrderImportService();
            var mapping = new XmlImportMappingSettings
            {
                AddressRecordElement = "AddrRow",
                OrderRecordElement = "OrderRow",
                ProductRecordElement = "PositionRow",
                AddressCompany = "Company",
                AddressStreet = "Street",
                OrderNumber = "OrderNo",
                OrderDate = "OrderDate",
                OrderAddressId = "AddressRef",
                ProductArticleNumber = "ItemNo",
                ProductDescription = "ItemName",
                ProductQuantity = "Qty",
                ProductWeight = "WeightKg",
                OrderDeliveryCondition = ""
            };

            var result = service.LoadOrdersFromFileDetailed(xmlPath, mapping);

            Assert.Single(result.Orders);
            Assert.Equal("A-201", result.Orders[0].AuftragNr);
            Assert.Equal("Muster AG", result.Orders[0].KundeFirma);
            Assert.Equal("Lieferung", result.Orders[0].Lieferbedingung);
            Assert.Single(result.Orders[0].Produkte);
            Assert.Equal("ART-77", result.Orders[0].Produkte[0].ArtikelNummer);
            Assert.Equal("Produkt B", result.Orders[0].Produkte[0].Bezeichnung);
            Assert.Equal(3m, result.Orders[0].Produkte[0].Menge);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
