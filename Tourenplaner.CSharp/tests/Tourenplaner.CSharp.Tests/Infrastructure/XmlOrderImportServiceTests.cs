using Tourenplaner.CSharp.Infrastructure.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public class XmlOrderImportServiceTests
{
    [Theory]
    [InlineData("Empfänger | Herr Meier | Industriestrasse 1 | CH-8000 Zürich | 12345", "Herr Meier", "8000 Zürich")]
    [InlineData("Empfänger | Betriebszentrum Parkhaus A | CH-8058 Zürich-Flughafen | 12345", "Betriebszentrum Parkhaus A", "8000 Zürich")]
    public void DeliveryAddressLineDoesNotMisclassifyContactOrOverrideDifferentBlock(string addressLine, string contact, string postalCity)
    {
        var file = Path.Combine(Path.GetTempPath(), $"import-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, $$"""
                <belege><beleg><ident>test</ident><kopf>test</kopf><datum>26.10.2026</datum>
                <adresskopflieferung>Empfänger
                {{contact}}
                {{postalCity}}</adresskopflieferung>
                <adresszeilelieferung>{{addressLine}}</adresszeilelieferung>
                </beleg></belege>
                """);
            var imported = Assert.Single(new XmlOrderImportService().LoadOrdersFromFileDetailed(file).Orders);
            Assert.Empty(imported.LieferStrasse);
            Assert.Equal(contact, imported.LieferKontaktperson);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("Betriebszentrum Parkhaus A")]
    [InlineData("Flughafenstrasse")]
    [InlineData("Flughafenstrasse 12")]
    public void DeliveryDestinationWithoutHouseNumberSurvivesImportAndMapping(string destination)
    {
        var file = Path.Combine(Path.GetTempPath(), $"import-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, $$"""
                <belege><beleg>
                <ident>airport</ident><kopf>222113</kopf><datum>26.10.2026</datum>
                <adresszeile>Hasler + Co AG | Rudolf-Diesel-Str. 12 | CH-8401 Winterthur | 100290</adresszeile>
                <lieferperson>Herr Nüssli</lieferperson>
                <adresskopflieferung>Flughafen Zürich AG
                Herr Nüssli
                {{destination}}
                8058 Zürich-Flughafen</adresskopflieferung>
                <adresszeilelieferung>Flughafen Zürich AG | {{destination}} | CH-8058 Zürich-Flughafen | 122424</adresszeilelieferung>
                </beleg></belege>
                """);
            var imported = Assert.Single(new XmlOrderImportService().LoadOrdersFromFileDetailed(file).Orders);
            Assert.Equal(destination, imported.LieferStrasse);
            Assert.Equal("Herr Nüssli", imported.LieferKontaktperson);
            Assert.Equal("8058", imported.LieferPLZ);
            Assert.Equal("Zürich-Flughafen", imported.LieferOrt);
            var mapped = new Tourenplaner.CSharp.Application.Services.OrderImportMapper().Map(imported);
            Assert.Equal(destination, mapped.DeliveryAddress.Street);
            Assert.Equal("8058", mapped.DeliveryAddress.PostalCode);
            Assert.Equal("Zürich-Flughafen", mapped.DeliveryAddress.City);
            Assert.Equal("Rudolf-Diesel-Str. 12", mapped.OrderAddress.Street);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("Verankerung: mit Verankerung", "Fracht_m_vert", "ART", "Mit Verteilung & Montage", false)]
    [InlineData("Montage: mit Montage", "Fracht_o_vert", "ART", "Mit Verteilung & Montage", false)]
    [InlineData("Montage: ohne Montage", "Fracht_m_vert", "ART", "Fracht_m_vert", false)]
    [InlineData("Verankerung: mit Verankerung\nVerankerung: ohne Verankerung", "Post", "ART", "Post", false)]
    [InlineData("<p>Andere Auswahl: blau</p><p><b>Verankerung:</b> mit&nbsp;Verankerung</p>", "Mit Verteilung", "ART", "Mit Verteilung & Montage", false)]
    [InlineData("Montage: mit Montage\nMontage: ohne Montage", "Mit Verteilung & Montage", "ART", "Mit Verteilung & Montage", true)]
    [InlineData("<b>Verankerung Tresore/Safe:</b> mit Verankerung (+CHF 90.00)", "Frei Bordsteinkante", "ART", "Mit Verteilung & Montage", false)]
    [InlineData("Verankerung Tresore/Safe: MIT   VERANKERUNG an Betonwand", "Mit Verteilung", "ART", "Mit Verteilung & Montage", false)]
    [InlineData("Verankerung: mit Verankerung", "Mit Verteilung & Montage", "ART", "Mit Verteilung & Montage", false)]
    [InlineData("Verankerung: ohne Verankerung", "Mit Verteilung", "ART", "Mit Verteilung", false)]
    [InlineData("Verankerung: mit Verankerungsmaterial", "Mit Verteilung", "ART", "Mit Verteilung", false)]
    [InlineData("Möglichkeit der Verankerung an der Wand. Lieferung mit Verankerung möglich.", "Mit Verteilung", "ART", "Mit Verteilung", false)]
    [InlineData("Verankerung: mit Verankerung", "Post", "ART", "Post", false)]
    [InlineData("Verankerung: mit Verankerung", "Spediteur", "ART", "Spediteur", false)]
    [InlineData("Verankerung: mit Verankerung", "Selbstabholung", "ART", "Selbstabholung", false)]
    [InlineData("Verankerung: mit Verankerung", "Tresor-Verwendung", "ART", "Tresor-Verwendung", false)]
    [InlineData("Verankerung: mit Verankerung", "Tresor-Bordstein", "ART", "Tresor-Bordstein", false)]
    [InlineData("Verankerung: mit Verankerung", "Mit Verteilung", "TXT", "Mit Verteilung", false)]
    [InlineData("Verankerung: mit Verankerung\nVerankerung: ohne Verankerung", "Mit Verteilung", "ART", "Mit Verteilung", true)]
    [InlineData("Montage Unterbau / Schrägdach: mit Montage (+CHF 49.00)", "Mit Verteilung", "ART", "Mit Verteilung & Montage", false)]
    [InlineData("<b>Montage Schaukästen:</b> MIT   MONTAGE (+CHF 89.00)", "Frei Bordsteinkante", "ART", "Mit Verteilung & Montage", false)]
    [InlineData("Montage Wandvitrine: mit Montage an Beton-/Backsteinwand (+CHF 135.00)", "Mit Verteilung", "ART", "Mit Verteilung & Montage", false)]
    [InlineData("Wandmontage inkl. Warenverteilung: mit Wandmontage inkl. Warenverteilung (+CHF 202.50)", "Mit Verteilung", "ART", "Mit Verteilung & Montage", false)]
    [InlineData("Montage Regale: ohne Montage", "Mit Verteilung", "ART", "Mit Verteilung", false)]
    [InlineData("Montage Pneuschränke: ohne Montage mit Montagematerial für Betonwand", "Mit Verteilung", "ART", "Mit Verteilung", false)]
    [InlineData("Montage: mit Montagematerial", "Mit Verteilung", "ART", "Mit Verteilung", false)]
    [InlineData("Lieferzeit verlängert sich durch Montage. Lieferung mit Montage möglich.", "Mit Verteilung", "ART", "Mit Verteilung", false)]
    [InlineData("", "Mit Verteilung", "ART", "Mit Verteilung", false)]
    [InlineData("Montage: mit Montage", "Post", "ART", "Post", false)]
    [InlineData("Montage: mit Montage", "Spediteur", "ART", "Spediteur", false)]
    [InlineData("Montage: mit Montage", "Selbstabholung", "ART", "Selbstabholung", false)]
    [InlineData("Montage: mit Montage", "Tresor-Verwendung", "ART", "Tresor-Verwendung", false)]
    [InlineData("Montage: mit Montage", "Tresor-Bordstein", "ART", "Tresor-Bordstein", false)]
    [InlineData("Montage: mit Montage", "Mit Verteilung", "TXT", "Mit Verteilung", false)]
    [InlineData("Montage: mit Montage\nMontage: ohne Montage", "Mit Verteilung", "ART", "Mit Verteilung", true)]
    [InlineData("Montage Unterbau: mit Montage<br/>Montage Regale: ohne Montage", "Mit Verteilung", "ART", "Mit Verteilung & Montage", false)]
    public void LoadOrdersFromFileDetailed_AssignsMontageOnlyForExplicitProductChoices(
        string text, string delivery, string positionCode, string expected, bool conflict)
    {
        var path = Path.GetTempFileName();
        try
        {
            var document = new System.Xml.Linq.XElement("belege",
                new System.Xml.Linq.XElement("beleg",
                    new System.Xml.Linq.XElement("ident", "order-1"),
                    new System.Xml.Linq.XElement("typ", "SALES"),
                    new System.Xml.Linq.XElement("kopf", "M-1"),
                    new System.Xml.Linq.XElement("datum", "21.10.2026"),
                    new System.Xml.Linq.XElement("versandart", delivery),
                    new System.Xml.Linq.XElement("positionen",
                        new System.Xml.Linq.XElement("position",
                            new System.Xml.Linq.XElement("kopfid", "order-1"),
                            new System.Xml.Linq.XElement("artikel", "PRODUCT"),
                            new System.Xml.Linq.XElement("bezeichnung", "Produkt"),
                            new System.Xml.Linq.XElement("menge", "2"),
                            new System.Xml.Linq.XElement("gewicht", "5"),
                            new System.Xml.Linq.XElement("poscode", positionCode),
                            new System.Xml.Linq.XElement("zusatztext", text)),
                        new System.Xml.Linq.XElement("position",
                            new System.Xml.Linq.XElement("kopfid", "order-1"),
                            new System.Xml.Linq.XElement("artikel", "OTHER"),
                            new System.Xml.Linq.XElement("bezeichnung", "Weiteres Produkt"),
                            new System.Xml.Linq.XElement("menge", "1"),
                            new System.Xml.Linq.XElement("gewicht", "3"),
                            new System.Xml.Linq.XElement("zusatztext", "Montage: ohne Montage")))));
            document.Save(path);
            var result = new XmlOrderImportService().LoadOrdersFromFileDetailed(path);
            Assert.Empty(result.Errors);
            var order = Assert.Single(result.Orders);
            Assert.Equal(expected, order.Lieferbedingung);
            Assert.Equal(positionCode == "TXT" ? 1 : 2, order.Produkte.Count);
            Assert.Equal(conflict, result.Warnings.Any(warning => warning.Contains("widersprüchliche Montage-/Verankerungsauswahl")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("RAB;ZWI;TXT", "poscode", "NS")]
    [InlineData("CUSTOM", "poscode", " ns ")]
    [InlineData("RAB;ZWI;TXT;NS", "positionstyp", "NS")]
    public void ImportExcludesNsWithExistingAndCustomSettings(string exclusions, string field, string code)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tourenplaner-ns-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(path, $"""
                <belege><beleg><ident>order-1</ident><kopf>A-1</kopf><datum>16.10.2026</datum><versandart>Post</versandart>
                <positionen>
                <position><kopfid>order-1</kopfid><{field}>{code}</{field}><artikel>NOT-A-PRODUCT</artikel><bezeichnung>Nicht importieren</bezeichnung><menge>1</menge></position>
                <position><kopfid>order-1</kopfid><{field}>ART</{field}><artikel>PRODUCT</artikel><bezeichnung>Produkt</bezeichnung><menge>2</menge><gewicht>3</gewicht></position>
                </positionen></beleg></belege>
                """);
            var mapping = XmlImportMappingSettings.CreateDefault();
            mapping.ExcludedProductPositionCodes = exclusions;
            mapping.ProductPositionCode = field;
            var result = new XmlOrderImportService().LoadOrdersFromFileDetailed(path, mapping);
            Assert.Empty(result.Errors);
            var product = Assert.Single(Assert.Single(result.Orders).Produkte);
            Assert.Equal("PRODUCT", product.ArtikelNummer);
            Assert.Equal(1, product.PosNummer);
            Assert.DoesNotContain(result.Warnings, x => x.Contains("Gewicht"));
            var effective = mapping.WithDefaults();
            Assert.Contains("NS", effective.ExcludedProductPositionCodes.Split(';'), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(effective.ExcludedProductPositionCodes, effective.WithDefaults().ExcludedProductPositionCodes);
            Assert.Contains(exclusions.Split(';')[0], effective.ExcludedProductPositionCodes);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Herr", "", "", "")]
    [InlineData("Frau", "", "", "")]
    [InlineData("Herr", "Erika Muster", "", "Erika Muster")]
    [InlineData("Herr", "", "<lieferperson>Erika Muster</lieferperson>", "Erika Muster")]
    public void PrivateDeliveryAddressKeepsOnlyAdditionalContact(string salutation, string extraLine, string contactElement, string expectedContact)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tourenplaner-private-address-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(path, $"""
                <belege><beleg>
                <ident>order-1</ident><kopf>A-1</kopf><datum>17.09.2026 00:00:00</datum>
                <adresszeile>Max Muster | Testweg 1 | CH-3150 Schwarzenburg | 123</adresszeile>
                {contactElement}
                <adresskopflieferung>{salutation}
                Max Muster
                {extraLine}
                Testweg 1
                3150 Schwarzenburg</adresskopflieferung>
                </beleg></belege>
                """);
            var order = Assert.Single(new XmlOrderImportService().LoadOrdersFromFileDetailed(path).Orders);
            Assert.Equal("Max Muster", order.LieferFirma);
            Assert.Equal(expectedContact, order.LieferKontaktperson);
            Assert.Equal("Testweg 1", order.LieferStrasse);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("belege", "beleg", "kopf", "datum", "Lieferdatum2", null)]
    [InlineData("belege", "beleg", "kopf", "datum", "lieferdatum2", null)]
    [InlineData("belege", "beleg", "kopf", "datum", "zus_liefertermin2", null)]
    [InlineData("NewDataSet", "WW_Kopf", "AuftragNr", "Datum", "Lieferdatum2", null)]
    [InlineData("belege", "beleg", "kopf", "datum", "zeitraum", "zeitraum")]
    public void LoadOrdersFromFile_ReadsDeliveryPeriodAsFreeText(string root, string record, string numberField,
        string dateField, string periodField, string? configuredField)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tourenplaner-period-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, $"""
            <{root}><{record}>
              <{numberField}>221878</{numberField}>
              <{dateField}>17.08.2026 00:00:00</{dateField}>
              <{periodField}> KW 43–44 &amp; nach Absprache </{periodField}>
            </{record}></{root}>
            """);
        try
        {
            var mapping = new XmlImportMappingSettings();
            if (configuredField is not null)
                mapping.OrderDeliveryPeriod = configuredField;
            var order = Assert.Single(new XmlOrderImportService().LoadOrdersFromFileDetailed(path, mapping).Orders);
            Assert.Equal("KW 43–44 & nach Absprache", order.Lieferdatum2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrdersFromFile_InterpretsDottedDatesAsDayMonthYear()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tourenplaner-date-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, """
            <belege>
              <beleg>
                <ident>order-id</ident>
                <typ>SALES</typ>
                <kopf>DATE-1</kopf>
                <datum>03.08.2026 00:00:00</datum>
                <lieferdatum>04.09.2026 00:00:00</lieferdatum>
              </beleg>
            </belege>
            """);

        try
        {
            var order = Assert.Single(new XmlOrderImportService().LoadOrdersFromFile(path));

            Assert.Equal(new DateTime(2026, 8, 3), order.AuftragsDatum);
            Assert.Equal(new DateTime(2026, 9, 4), order.Lieferdatum);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
                    <firma>A GmbH | Konstanzerstrasse 14 | CH-8274 Tägerwilen | 100038</firma>
                    <standort>Frau Janine Fäsi | Ara-Strasse 6 | CH-8274 Tägerwilen | 200039</standort>
                    <LiefKondID>Lieferung</LiefKondID>
                    <Lieferdatum>2026-06-11T00:00:00</Lieferdatum>
                    <Archiviert>false</Archiviert>
                    <Notiz>Testnotiz</Notiz>
                  </WW_Kopf>
                  <WW_Pos>
                    <KopfID>order-1</KopfID>
                    <ArtikelID>PRODUKT-A</ArtikelID>
                    <Bezeichnung>Produkt A</Bezeichnung>
                    <lieferant>Esnova Racks S.A. | Plg. Los Campones s/n - Tremanes | ES-33211 Gijon | 111362</lieferant>
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
            Assert.Equal("100038", result.Orders[0].KundeAdressNummer);
            Assert.Equal("Empfaenger GmbH", result.Orders[0].LieferFirma);
            Assert.Equal("200039", result.Orders[0].LieferAdressNummer);
            Assert.Equal(new DateTime(2026, 6, 11), result.Orders[0].Lieferdatum);
            Assert.Single(result.Orders[0].Produkte);
            Assert.Equal("PRODUKT-A", result.Orders[0].Produkte[0].ArtikelNummer);
            Assert.Equal("Esnova Racks S.A.", result.Orders[0].Produkte[0].Lieferant);
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
            Assert.Null(result.Orders[0].Lieferdatum);
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
    public void LoadOrdersFromFileDetailed_TreatsMinValueDeliveryDateAsMissing()
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
                    <Strasse>Musterstrasse 10</Strasse>
                    <PLZ>8000</PLZ>
                    <Ort>Zuerich</Ort>
                  </AVE_Stamm>
                  <WW_Kopf>
                    <Ident>order-1</Ident>
                    <AuftragNr>A-202</AuftragNr>
                    <Datum>2026-06-10T00:00:00</Datum>
                    <AdressID>100</AdressID>
                    <LiefKondID>Lieferung</LiefKondID>
                    <Lieferdatum>0001-01-01T00:00:00</Lieferdatum>
                    <Archiviert>false</Archiviert>
                  </WW_Kopf>
                </NewDataSet>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            var order = Assert.Single(result.Orders);
            Assert.Equal("A-202", order.AuftragNr);
            Assert.Null(order.Lieferdatum);
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
    public void LoadOrdersFromFileDetailed_MapsDirectDeliveryArticleToTresorBordsteinBeforeShippingMethod()
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
                    <versandart>LKW</versandart>
                    <archiv>False</archiv>
                    <adresszeile>Auftraggeber Zeile AG | Hauptstrasse 12 | CH-7001 Chur | 123456</adresszeile>
                    <adresskopf>Auftraggeber AG
                    Auftragsweg 3
                    7000 Chur</adresskopf>
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
                        <menge>-5</menge>
                        <bezeichnung>Produkt D</bezeichnung>
                        <gewicht>2.5</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>Fracht-Direktlieferung</artikel>
                        <menge>1</menge>
                        <bezeichnung>Fracht mit Direktlieferung von der Produktion</bezeichnung>
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
            Assert.Equal("Tresor-Bordstein", result.Orders[0].Lieferbedingung);
            Assert.Equal("Auftraggeber Zeile AG", result.Orders[0].KundeFirma);
            Assert.Equal("Hauptstrasse 12", result.Orders[0].KundeStrasse);
            Assert.Equal("7001", result.Orders[0].KundePLZ);
            Assert.Equal("Chur", result.Orders[0].KundeOrt);
            Assert.Equal("123456", result.Orders[0].KundeAdressNummer);
            Assert.Equal("Liefer AG", result.Orders[0].LieferFirma);
            Assert.Single(result.Orders[0].Produkte);
            Assert.Equal("PRODUKT-D", result.Orders[0].Produkte[0].ArtikelNummer);
            Assert.Equal("Produkt D", result.Orders[0].Produkte[0].Bezeichnung);
            Assert.Equal(-5m, result.Orders[0].Produkte[0].Menge);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_SeparatesBelegCompanyAndContactPerson()
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
                    <kopf>221839</kopf>
                    <datum>08.07.2026 00:00:00</datum>
                    <person>Andreas Gehrig</person>
                    <lieferperson>Andreas Gehrig</lieferperson>
                    <versandart>LKW</versandart>
                    <archiv>False</archiv>
                    <adresskopfrechnung>Kurhaus am Sarnersee
                    Wilerstrasse 35
                    6062 Wilen (Sarnen)</adresskopfrechnung>
                    <adresskopflieferung>Kurhaus am Sarnersee
                    Andreas Gehrig
                    Wilerstrasse 35
                    6062 Wilen (Sarnen)</adresskopflieferung>
                    <positionen>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>PRODUKT-E</artikel>
                        <menge>1</menge>
                        <bezeichnung>Produkt E</bezeichnung>
                        <gewicht>2.5</gewicht>
                      </position>
                    </positionen>
                  </beleg>
                </belege>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Single(result.Orders);
            var order = result.Orders[0];
            Assert.Equal("Kurhaus am Sarnersee", order.KundeFirma);
            Assert.Equal("Andreas Gehrig", order.KundeKontaktperson);
            Assert.Equal("Kurhaus am Sarnersee", order.LieferFirma);
            Assert.Equal("Andreas Gehrig", order.LieferKontaktperson);
            Assert.Equal("Wilerstrasse 35", order.LieferStrasse);
            Assert.Equal("6062", order.LieferPLZ);
            Assert.Equal("Wilen (Sarnen)", order.LieferOrt);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_DoesNotTreatContactOrInternalAddressNumberAsStreetAddress()
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
                    <kopf>222059</kopf>
                    <datum>12.10.2026 00:00:00</datum>
                    <archiv>False</archiv>
                    <lieferperson>Philipp Winter</lieferperson>
                    <adresskopfrechnung>VBS
                    Guisanplatz 1
                    3003 Bern</adresskopfrechnung>
                    <adresskopflieferung>Logistikbasis der Armee LBA
                    LBA Aussenstelle Emmen
                    Philipp Winter
                    6032 Emmen</adresskopflieferung>
                    <standort>Logistikbasis der Armee LBA | CH-6032 Emmen | 122358</standort>
                    <positionen>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>PRODUKT</artikel>
                        <menge>1</menge>
                        <bezeichnung>Produkt</bezeichnung>
                        <gewicht>1</gewicht>
                      </position>
                    </positionen>
                  </beleg>
                </belege>
                """);

            var result = new XmlOrderImportService().LoadOrdersFromFileDetailed(xmlPath);

            var order = Assert.Single(result.Orders);
            Assert.Equal("122358", order.LieferAdressNummer);
            Assert.Empty(order.LieferStrasse);
            Assert.Equal("6032", order.LieferPLZ);
            Assert.Equal("Emmen", order.LieferOrt);
            Assert.Contains("Philipp Winter", order.LieferKontaktperson);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_UsesNameAfterSalutationInBelegOrderAddress()
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
                    <kopf>A-305</kopf>
                    <datum>15.07.2026 00:00:00</datum>
                    <adresskopf>Herr
                    Mike Saint
                    Ara-Strasse 6
                    8274 Taegerwilen</adresskopf>
                  </beleg>
                </belege>
                """);

            var order = Assert.Single(new XmlOrderImportService().LoadOrdersFromFileDetailed(xmlPath).Orders);

            Assert.Equal("Mike Saint", order.KundeFirma);
            Assert.Empty(order.KundeKontaktperson);
            Assert.Equal("Ara-Strasse 6", order.KundeStrasse);
            Assert.Equal("8274", order.KundePLZ);
            Assert.Equal("Taegerwilen", order.KundeOrt);
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
    public void LoadOrdersFromFileDetailed_SkipsDefaultExcludedPositionCodes()
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
                    <kopf>A-306</kopf>
                    <datum>15.07.2026 00:00:00</datum>
                    <versandart>Post</versandart>
                    <archiv>False</archiv>
                    <positionen>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>PRODUKT-G</artikel>
                        <menge>1</menge>
                        <bezeichnung>Produkt G</bezeichnung>
                        <gewicht>2.5</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <poscode>RAB</poscode>
                        <artikel>RABATT</artikel>
                        <menge>1</menge>
                        <bezeichnung>Rabattposition</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <poscode>zwi</poscode>
                        <artikel>ZWISCHENZEILE</artikel>
                        <menge>1</menge>
                        <bezeichnung>Zwischenzeile</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <poscode>TXT</poscode>
                        <artikel>TEXTZEILE</artikel>
                        <menge>1</menge>
                        <bezeichnung>Textzeile</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                    </positionen>
                  </beleg>
                </belege>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Single(result.Orders);
            Assert.Single(result.Orders[0].Produkte);
            Assert.Equal("PRODUKT-G", result.Orders[0].Produkte[0].ArtikelNummer);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_UsesCombinedMontageAndDistributionMarkers()
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
                    <kopf>A-307</kopf>
                    <datum>15.07.2026 00:00:00</datum>
                    <archiv>False</archiv>
                    <positionen>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>PRODUKT-H</artikel>
                        <menge>1</menge>
                        <bezeichnung>Produkt H</bezeichnung>
                        <gewicht>2.5</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikelid>973d1a2f-155b-11ec-8a65-40b076de1f8f</artikelid>
                        <menge>1</menge>
                        <bezeichnung>Montagekosten</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikelid>1be04ec6-080a-11ec-8a64-40b076de1f8f</artikelid>
                        <menge>1</menge>
                        <bezeichnung>Lieferkosten mit Warenverteilung</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                    </positionen>
                  </beleg>
                </belege>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Single(result.Orders);
            Assert.Equal(DeliveryMethodExtensions.MitVerteilungMontage, result.Orders[0].Lieferbedingung);
            Assert.Single(result.Orders[0].Produkte);
            Assert.Equal("PRODUKT-H", result.Orders[0].Produkte[0].ArtikelNummer);
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
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("keine Lieferadresse gefunden", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_AddsWarningWhenImportedProductHasNoWeight()
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
                    <kopf>A-306</kopf>
                    <datum>15.07.2026 00:00:00</datum>
                    <versandart>Post</versandart>
                    <archiv>False</archiv>
                    <positionen>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>PRODUKT-G</artikel>
                        <menge>1</menge>
                        <bezeichnung>Produkt G</bezeichnung>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>TEXT</artikel>
                        <menge>1</menge>
                        <bezeichnung>Textblock Hinweis</bezeichnung>
                      </position>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>PRODUKT-H</artikel>
                        <menge>1</menge>
                        <bezeichnung>Produkt H</bezeichnung>
                        <gewicht>0</gewicht>
                      </position>
                    </positionen>
                  </beleg>
                </belege>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Single(result.Orders);
            Assert.Equal(2, result.Orders[0].Produkte.Count);
            Assert.Equal(0m, result.Orders[0].Produkte[0].Gewicht);
            Assert.Equal(0m, result.Orders[0].Produkte[1].Gewicht);
            Assert.Contains(result.Warnings, warning => warning.Contains("Produktposition ohne Gewichtsangabe", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadOrdersFromFileDetailed_DoesNotWarnAboutMissingDeliveryAddressForPickup()
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
                    <kopf>A-306</kopf>
                    <datum>15.07.2026 00:00:00</datum>
                    <versandart>Selbstabholung</versandart>
                    <adresskopfrechnung>Abholkunde AG
                    Musterstrasse 1
                    8000 Zuerich</adresskopfrechnung>
                    <zahlkondition>Vorkasse</zahlkondition>
                    <vorauszahlung>OK</vorauszahlung>
                    <archiv>False</archiv>
                    <positionen>
                      <position>
                        <kopfid>order-1</kopfid>
                        <artikel>PRODUKT-A</artikel>
                        <menge>1</menge>
                        <bezeichnung>Produkt A</bezeichnung>
                        <gewicht>10</gewicht>
                      </position>
                    </positionen>
                  </beleg>
                </belege>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Single(result.Orders);
            Assert.Equal("Selbstabholung", result.Orders[0].Lieferbedingung);
            Assert.False(result.Orders[0].IstVorauszahlung);
            Assert.True(result.Orders[0].IstVorauszahlungBezahlt);
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("keine Lieferadresse gefunden", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("Offen", true, false)]
    [InlineData("OK", false, true)]
    [InlineData("-", false, false)]
    public void LoadOrdersFromFileDetailed_MapsExportedPrepaymentStatus(string status, bool isOpen, bool isPaid)
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "orders.xml");

        try
        {
            File.WriteAllText(xmlPath, $"""
                <belege>
                  <beleg>
                    <ident>order-1</ident>
                    <kopf>A-307</kopf>
                    <datum>15.07.2026 00:00:00</datum>
                    <vorauszahlung>{status}</vorauszahlung>
                  </beleg>
                </belege>
                """);

            var order = Assert.Single(new XmlOrderImportService().LoadOrdersFromFileDetailed(xmlPath).Orders);

            Assert.Equal(isOpen, order.IstVorauszahlung);
            Assert.Equal(isPaid, order.IstVorauszahlungBezahlt);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
