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
                OrderId = "Ident",
                OrderNumber = "OrderNo",
                OrderDate = "OrderDate",
                OrderAddressId = "AddressRef",
                OrderDeliveryCondition = "LiefKondID",
                ProductOrderId = "KopfID",
                ProductArticleNumber = "ItemNo",
                ProductDescription = "ItemName",
                ProductQuantity = "Qty",
                ProductWeight = "WeightKg"
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

    [Fact]
    public void LoadOrdersFromFileDetailed_DoesNotDetectDeliveryTypeFromProductDescription()
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
                  </AVE_Stamm>
                  <WW_Kopf>
                    <Ident>order-1</Ident>
                    <AuftragNr>A-300</AuftragNr>
                    <Typ>SALES</Typ>
                    <Datum>2026-06-10T00:00:00</Datum>
                    <AdressID>100</AdressID>
                    <LiefKondID></LiefKondID>
                    <Archiviert>false</Archiviert>
                  </WW_Kopf>
                  <WW_Pos>
                    <KopfID>order-1</KopfID>
                    <ArtikelID>PRODUKT-MARKER</ArtikelID>
                    <Menge>0</Menge>
                    <Bezeichnung>Frei Bordsteinkante</Bezeichnung>
                  </WW_Pos>
                  <WW_Pos>
                    <KopfID>order-1</KopfID>
                    <ArtikelID>PRODUKT-B</ArtikelID>
                    <Menge>1.000000</Menge>
                    <Bezeichnung>Produkt B</Bezeichnung>
                    <Gewicht>2.5</Gewicht>
                  </WW_Pos>
                </NewDataSet>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Single(result.Orders);
            Assert.Equal("A-300", result.Orders[0].AuftragNr);
            Assert.Equal("Selbstabholung", result.Orders[0].Lieferbedingung);
            Assert.Equal(2, result.Orders[0].Produkte.Count);
            Assert.Equal("PRODUKT-MARKER", result.Orders[0].Produkte[0].ArtikelNummer);
            Assert.Equal("Frei Bordsteinkante", result.Orders[0].Produkte[0].Bezeichnung);
            Assert.Equal("PRODUKT-B", result.Orders[0].Produkte[1].ArtikelNummer);
            Assert.Equal("Produkt B", result.Orders[0].Produkte[1].Bezeichnung);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_DetectsDeliveryTypeFromProductArticleNumber()
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
                  </AVE_Stamm>
                  <WW_Kopf>
                    <Ident>order-1</Ident>
                    <AuftragNr>A-301</AuftragNr>
                    <Typ>SALES</Typ>
                    <Datum>2026-06-10T00:00:00</Datum>
                    <AdressID>100</AdressID>
                    <LiefKondID></LiefKondID>
                    <Archiviert>false</Archiviert>
                  </WW_Kopf>
                  <WW_Pos>
                    <KopfID>order-1</KopfID>
                    <ArtikelID>FRACHT-O-VERT</ArtikelID>
                    <Menge>0</Menge>
                    <Bezeichnung>Nicht relevante Bezeichnung</Bezeichnung>
                  </WW_Pos>
                  <WW_Pos>
                    <KopfID>order-1</KopfID>
                    <ArtikelID>PRODUKT-C</ArtikelID>
                    <Menge>2.000000</Menge>
                    <Bezeichnung>Produkt C</Bezeichnung>
                    <Gewicht>1.2</Gewicht>
                  </WW_Pos>
                </NewDataSet>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Single(result.Orders);
            Assert.Equal("A-301", result.Orders[0].AuftragNr);
            Assert.Equal("Frei Bordsteinkante", result.Orders[0].Lieferbedingung);
            Assert.Single(result.Orders[0].Produkte);
            Assert.Equal("PRODUKT-C", result.Orders[0].Produkte[0].ArtikelNummer);
            Assert.Equal("Produkt C", result.Orders[0].Produkte[0].Bezeichnung);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_UsesBelegProductArticleNumberBeforeShippingMethod()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "orders.xml");

        try
        {
            File.WriteAllText(xmlPath,
                """
                <belege>
                  <beleg>
                    <ident>order-1</ident>
                    <typ>SALES</typ>
                    <kopf>A-302</kopf>
                    <datum>15.07.2026 00:00:00</datum>
                    <versandart>Post</versandart>
                    <archiv>False</archiv>
                    <adresskopfrechnung>Rechnung AG
                    Rechnungsweg 1
                    8000 Zuerich</adresskopfrechnung>
                    <adresskopflieferung>Liefer AG
                    Lieferweg 2
                    9000 St. Gallen</adresskopflieferung>
                    <positionen>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>PRODUKT-D</artikel>
                        <menge>1</menge>
                        <bezeichnung>Produkt D</bezeichnung>
                        <gewicht>2.5</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>FRACHT-M-VERT</artikel>
                        <menge>1</menge>
                        <bezeichnung>Fracht- / Lieferkosten mit einer GAWELA Liefertour mit Warenverteilung</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                    </positionen>
                  </beleg>
                </belege>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Single(result.Orders);
            Assert.Equal("A-302", result.Orders[0].AuftragNr);
            Assert.Equal("Mit Verteilung", result.Orders[0].Lieferbedingung);
            Assert.Equal("Rechnung AG", result.Orders[0].KundeFirma);
            Assert.Equal("Liefer AG", result.Orders[0].LieferFirma);
            Assert.Single(result.Orders[0].Produkte);
            Assert.Equal("PRODUKT-D", result.Orders[0].Produkte[0].ArtikelNummer);
            Assert.Equal("Produkt D", result.Orders[0].Produkte[0].Bezeichnung);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_PreservesCustomDeliveryTypeArticleNumbersForBelegExport()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "orders.xml");

        try
        {
            File.WriteAllText(xmlPath,
                """
                <belege>
                  <beleg>
                    <ident>order-1</ident>
                    <typ>SALES</typ>
                    <kopf>A-303</kopf>
                    <datum>15.07.2026 00:00:00</datum>
                    <versandart>Post</versandart>
                    <archiv>False</archiv>
                    <positionen>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>PRODUKT-E</artikel>
                        <menge>1</menge>
                        <bezeichnung>Produkt E</bezeichnung>
                        <gewicht>2.5</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>CUSTOM-FRACHT</artikel>
                        <menge>1</menge>
                        <bezeichnung>Benutzerdefinierte Frachtposition</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                    </positionen>
                  </beleg>
                </belege>
                """);

            var mapping = new XmlImportMappingSettings
            {
                DeliveryTypeMitVerteilungArticleNumbers = "CUSTOM-FRACHT"
            };

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath, mapping);

            Assert.Single(result.Orders);
            Assert.Equal("A-303", result.Orders[0].AuftragNr);
            Assert.Equal("Mit Verteilung", result.Orders[0].Lieferbedingung);
            Assert.Single(result.Orders[0].Produkte);
            Assert.Equal("PRODUKT-E", result.Orders[0].Produkte[0].ArtikelNummer);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_SkipsConfiguredProductExclusions()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "orders.xml");

        try
        {
            File.WriteAllText(xmlPath,
                """
                <belege>
                  <beleg>
                    <ident>order-1</ident>
                    <typ>SALES</typ>
                    <kopf>A-304</kopf>
                    <datum>15.07.2026 00:00:00</datum>
                    <versandart>Post</versandart>
                    <archiv>False</archiv>
                    <positionen>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>PRODUKT-F</artikel>
                        <menge>1</menge>
                        <bezeichnung>Produkt F</bezeichnung>
                        <gewicht>2.5</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>ZERO-WEIGHT</artikel>
                        <menge>1</menge>
                        <bezeichnung>Echter Artikel ohne Gewicht</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <menge>1</menge>
                        <bezeichnung>Zwischentotal</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>IGNORE-ME</artikel>
                        <menge>1</menge>
                        <bezeichnung>Interne Textzeile</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>DISC-10</artikel>
                        <menge>1</menge>
                        <bezeichnung>Rabatt 10%</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                    </positionen>
                  </beleg>
                </belege>
                """);

            var mapping = new XmlImportMappingSettings
            {
                ExcludedProductArticleNumbers = "IGNORE-ME",
                ExcludedProductDescriptions = "Zwischentotal;Rabatt"
            };

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath, mapping);

            Assert.Single(result.Orders);
            Assert.Equal("A-304", result.Orders[0].AuftragNr);
            Assert.Equal(2, result.Orders[0].Produkte.Count);
            Assert.Equal("PRODUKT-F", result.Orders[0].Produkte[0].ArtikelNummer);
            Assert.Equal("ZERO-WEIGHT", result.Orders[0].Produkte[1].ArtikelNummer);
            Assert.Equal(0m, result.Orders[0].Produkte[1].Gewicht);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_AddsWarningsForSilentAssumptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "orders.xml");

        try
        {
            File.WriteAllText(xmlPath,
                """
                <belege>
                  <beleg>
                    <ident>order-1</ident>
                    <typ>SALES</typ>
                    <kopf>A-305</kopf>
                    <datum>15.07.2026 00:00:00</datum>
                    <archiv>False</archiv>
                    <positionen>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>TEXT</artikel>
                        <menge>1</menge>
                        <bezeichnung>Textblock Montagehinweis</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>DISC-10</artikel>
                        <menge>1</menge>
                        <bezeichnung>Rabatt 10%</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                    </positionen>
                  </beleg>
                </belege>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Single(result.Orders);
            Assert.Equal("A-305", result.Orders[0].AuftragNr);
            Assert.Empty(result.Orders[0].Produkte);
            Assert.Contains(result.Warnings, warning => warning.Contains("keine Lieferart erkannt", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("keine Lieferadresse gefunden", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
