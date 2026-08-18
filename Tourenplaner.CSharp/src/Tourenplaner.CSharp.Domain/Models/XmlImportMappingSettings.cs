using System.Collections.Generic;
using System.Linq;

namespace Tourenplaner.CSharp.Domain.Models;

public sealed class XmlImportMappingSettings
{
    public const string LegacyAddressRecordElement = "AVE_Stamm";
    public const string LegacyOrderRecordElement = "WW_Kopf";
    public const string LegacyProductRecordElement = "WW_Pos";

    public const string LegacyAddressId = "Adresse";
    public const string LegacyAddressCompany = "Firma";
    public const string LegacyAddressLastName = "Nachname";
    public const string LegacyAddressFirstName = "Vorname";
    public const string LegacyAddressStreet = "Strasse";
    public const string LegacyAddressHouseNumber = "";
    public const string LegacyAddressPostalCode = "PLZ";
    public const string LegacyAddressCity = "Ort";
    public const string LegacyAddressCountry = "Land";
    public const string LegacyAddressEmail = "Email";
    public const string LegacyAddressPhone = "Telefon";
    public const string LegacyAddressContactPerson = "Kontaktperson";

    public const string LegacyOrderId = "Ident";
    public const string LegacyOrderNumber = "AuftragNr";
    public const string LegacyOrderType = "Typ";
    public const string LegacyOrderDate = "Datum";
    public const string LegacyOrderAddressId = "AdressID";
    public const string LegacyOrderDeliveryAddressId = "LieferadressID";
    public const string LegacyOrderDeliveryCondition = "LiefKondID";
    public const string LegacyOrderDeliveryDate = "Lieferdatum";
    public const string LegacyOrderDeliveryCanOccurEarlier = "LieferdatumFrueher";
    public const string LegacyOrderArchived = "Archiviert";
    public const string LegacyOrderLocked = "";
    public const string LegacyOrderNote = "Notiz";

    public const string LegacyProductOrderId = "KopfID";
    public const string LegacyProductArticleNumber = "ArtikelID";
    public const string LegacyProductDescription = "Bezeichnung";
    public const string LegacyProductQuantity = "Menge";
    public const string LegacyProductWeight = "Gewicht";

    public const string DefaultAddressRecordElement = LegacyAddressRecordElement;
    public const string DefaultOrderRecordElement = "beleg";
    public const string DefaultProductRecordElement = "position";

    public const string DefaultAddressId = LegacyAddressId;
    public const string DefaultAddressCompany = LegacyAddressCompany;
    public const string DefaultAddressLastName = LegacyAddressLastName;
    public const string DefaultAddressFirstName = LegacyAddressFirstName;
    public const string DefaultAddressStreet = LegacyAddressStreet;
    public const string DefaultAddressHouseNumber = LegacyAddressHouseNumber;
    public const string DefaultAddressPostalCode = LegacyAddressPostalCode;
    public const string DefaultAddressCity = LegacyAddressCity;
    public const string DefaultAddressCountry = LegacyAddressCountry;
    public const string DefaultAddressEmail = LegacyAddressEmail;
    public const string DefaultAddressPhone = LegacyAddressPhone;
    public const string DefaultAddressContactPerson = LegacyAddressContactPerson;

    public const string DefaultOrderBillingAddressBlock = "adresskopfrechnung";
    public const string DefaultOrderDeliveryAddressBlock = "adresskopflieferung";
    public const string DefaultOrderId = "ident";
    public const string DefaultOrderNumber = "kopf";
    public const string DefaultOrderType = "typ";
    public const string DefaultOrderDate = "datum";
    public const string DefaultOrderAddressId = "adressid";
    public const string DefaultOrderDeliveryAddressId = "";
    public const string DefaultOrderAddressNumber = "firma";
    public const string DefaultOrderDeliveryAddressNumber = "Standort";
    public const string DefaultOrderDeliveryCondition = "versandart";
    public const string DefaultOrderDeliveryDate = "lieferdatum";
    public const string DefaultOrderDeliveryCanOccurEarlier = "lieferdatumfrüher";
    public const string DefaultOrderDeliveryTime = "zus_lieferzeit";
    public const string DefaultOrderArchived = "archiv";
    public const string DefaultOrderLocked = "sperre";
    public const string DefaultOrderNote = "notiz";

    public const string DefaultProductOrderId = "kopfid";
    public const string DefaultProductArticleNumber = "artikel";
    public const string DefaultProductDescription = "bezeichnung";
    public const string DefaultProductQuantity = "menge";
    public const string DefaultProductWeight = "gewicht";
    public const string DefaultExcludedProductArticleNumbers = "";
    public const string DefaultExcludedProductDescriptions = "Zwischentotal;Zwischensumme;Subtotal;Textblock;Textblöcke;Rabatt";

    public const string DefaultDeliveryTypeFreiBordsteinkanteArticleNumbers = "FRACHT-O-VERT;FRACHT-O-VERT-alt";
    public const string DefaultDeliveryTypeMitVerteilungArticleNumbers = "FRACHT-M-VERT;FRACHT-M-VERT-Pneu";
    public const string DefaultDeliveryTypeMitVerteilungMontageArticleNumbers = "FRACHT-M-VERT-MONT";
    public const string DefaultDeliveryTypeSpediteurArticleNumbers = "Fracht mit Spediteur";
    public const string DefaultDeliveryTypePostArticleNumbers = "POST";
    public const string DefaultDeliveryTypeTresorBordsteinArticleNumbers = "FRACHT-TRESOR-BORDSTEIN";
    public const string DefaultDeliveryTypeTresorVerwendungArticleNumbers = "FRACHT-TRESOR-VERWENDUNG";
    public const string DefaultDeliveryTypeSelbstabholungArticleNumbers = "Selbstabholung";

    public string AddressRecordElement { get; set; } = DefaultAddressRecordElement;
    public string OrderRecordElement { get; set; } = DefaultOrderRecordElement;
    public string ProductRecordElement { get; set; } = DefaultProductRecordElement;

    public string AddressId { get; set; } = DefaultAddressId;
    public string AddressCompany { get; set; } = DefaultAddressCompany;
    public string AddressLastName { get; set; } = DefaultAddressLastName;
    public string AddressFirstName { get; set; } = DefaultAddressFirstName;
    public string AddressStreet { get; set; } = DefaultAddressStreet;
    public string AddressHouseNumber { get; set; } = DefaultAddressHouseNumber;
    public string AddressPostalCode { get; set; } = DefaultAddressPostalCode;
    public string AddressCity { get; set; } = DefaultAddressCity;
    public string AddressCountry { get; set; } = DefaultAddressCountry;
    public string AddressEmail { get; set; } = DefaultAddressEmail;
    public string AddressPhone { get; set; } = DefaultAddressPhone;
    public string AddressContactPerson { get; set; } = DefaultAddressContactPerson;

    public string OrderBillingAddressBlock { get; set; } = DefaultOrderBillingAddressBlock;
    public string OrderDeliveryAddressBlock { get; set; } = DefaultOrderDeliveryAddressBlock;
    public string OrderId { get; set; } = DefaultOrderId;
    public string OrderNumber { get; set; } = DefaultOrderNumber;
    public string OrderType { get; set; } = DefaultOrderType;
    public string OrderDate { get; set; } = DefaultOrderDate;
    public string OrderAddressId { get; set; } = DefaultOrderAddressId;
    public string OrderDeliveryAddressId { get; set; } = DefaultOrderDeliveryAddressId;
    public string OrderAddressNumber { get; set; } = DefaultOrderAddressNumber;
    public string OrderDeliveryAddressNumber { get; set; } = DefaultOrderDeliveryAddressNumber;
    public string OrderDeliveryCondition { get; set; } = DefaultOrderDeliveryCondition;
    public string OrderDeliveryDate { get; set; } = DefaultOrderDeliveryDate;
    public string OrderDeliveryCanOccurEarlier { get; set; } = DefaultOrderDeliveryCanOccurEarlier;
    public string OrderDeliveryTime { get; set; } = DefaultOrderDeliveryTime;
    public string OrderArchived { get; set; } = DefaultOrderArchived;
    public string OrderLocked { get; set; } = DefaultOrderLocked;
    public string OrderNote { get; set; } = DefaultOrderNote;

    public string ProductOrderId { get; set; } = DefaultProductOrderId;
    public string ProductArticleNumber { get; set; } = DefaultProductArticleNumber;
    public string ProductDescription { get; set; } = DefaultProductDescription;
    public string ProductQuantity { get; set; } = DefaultProductQuantity;
    public string ProductWeight { get; set; } = DefaultProductWeight;
    public string ExcludedProductArticleNumbers { get; set; } = DefaultExcludedProductArticleNumbers;
    public string ExcludedProductDescriptions { get; set; } = DefaultExcludedProductDescriptions;

    public string DeliveryTypeFreiBordsteinkanteArticleNumbers { get; set; } = DefaultDeliveryTypeFreiBordsteinkanteArticleNumbers;
    public string DeliveryTypeMitVerteilungArticleNumbers { get; set; } = DefaultDeliveryTypeMitVerteilungArticleNumbers;
    public string DeliveryTypeMitVerteilungMontageArticleNumbers { get; set; } = DefaultDeliveryTypeMitVerteilungMontageArticleNumbers;
    public string DeliveryTypeSpediteurArticleNumbers { get; set; } = DefaultDeliveryTypeSpediteurArticleNumbers;
    public string DeliveryTypePostArticleNumbers { get; set; } = DefaultDeliveryTypePostArticleNumbers;
    public string DeliveryTypeTresorBordsteinArticleNumbers { get; set; } = DefaultDeliveryTypeTresorBordsteinArticleNumbers;
    public string DeliveryTypeTresorVerwendungArticleNumbers { get; set; } = DefaultDeliveryTypeTresorVerwendungArticleNumbers;
    public string DeliveryTypeSelbstabholungArticleNumbers { get; set; } = DefaultDeliveryTypeSelbstabholungArticleNumbers;

    public static XmlImportMappingSettings CreateDefault()
    {
        return new XmlImportMappingSettings();
    }

    public XmlImportMappingSettings WithDefaults()
    {
        var effective = new XmlImportMappingSettings
        {
            AddressRecordElement = Normalize(AddressRecordElement, DefaultAddressRecordElement),
            OrderRecordElement = Normalize(OrderRecordElement, DefaultOrderRecordElement),
            ProductRecordElement = Normalize(ProductRecordElement, DefaultProductRecordElement),
            AddressId = Normalize(AddressId, DefaultAddressId),
            AddressCompany = Normalize(AddressCompany, DefaultAddressCompany),
            AddressLastName = Normalize(AddressLastName, DefaultAddressLastName),
            AddressFirstName = Normalize(AddressFirstName, DefaultAddressFirstName),
            AddressStreet = Normalize(AddressStreet, DefaultAddressStreet),
            AddressHouseNumber = Normalize(AddressHouseNumber, DefaultAddressHouseNumber),
            AddressPostalCode = Normalize(AddressPostalCode, DefaultAddressPostalCode),
            AddressCity = Normalize(AddressCity, DefaultAddressCity),
            AddressCountry = Normalize(AddressCountry, DefaultAddressCountry),
            AddressEmail = Normalize(AddressEmail, DefaultAddressEmail),
            AddressPhone = Normalize(AddressPhone, DefaultAddressPhone),
            AddressContactPerson = Normalize(AddressContactPerson, DefaultAddressContactPerson),
            OrderBillingAddressBlock = Normalize(OrderBillingAddressBlock, DefaultOrderBillingAddressBlock),
            OrderDeliveryAddressBlock = Normalize(OrderDeliveryAddressBlock, DefaultOrderDeliveryAddressBlock),
            OrderId = Normalize(OrderId, DefaultOrderId),
            OrderNumber = Normalize(OrderNumber, DefaultOrderNumber),
            OrderType = Normalize(OrderType, DefaultOrderType),
            OrderDate = Normalize(OrderDate, DefaultOrderDate),
            OrderAddressId = Normalize(OrderAddressId, DefaultOrderAddressId),
            OrderDeliveryAddressId = Normalize(OrderDeliveryAddressId, DefaultOrderDeliveryAddressId),
            OrderAddressNumber = Normalize(OrderAddressNumber, DefaultOrderAddressNumber),
            OrderDeliveryAddressNumber = Normalize(OrderDeliveryAddressNumber, DefaultOrderDeliveryAddressNumber),
            OrderDeliveryCondition = Normalize(OrderDeliveryCondition, DefaultOrderDeliveryCondition),
            OrderDeliveryDate = Normalize(OrderDeliveryDate, DefaultOrderDeliveryDate),
            OrderDeliveryCanOccurEarlier = Normalize(OrderDeliveryCanOccurEarlier, DefaultOrderDeliveryCanOccurEarlier),
            OrderDeliveryTime = Normalize(OrderDeliveryTime, DefaultOrderDeliveryTime),
            OrderArchived = Normalize(OrderArchived, DefaultOrderArchived),
            OrderLocked = Normalize(OrderLocked, DefaultOrderLocked),
            OrderNote = Normalize(OrderNote, DefaultOrderNote),
            ProductOrderId = Normalize(ProductOrderId, DefaultProductOrderId),
            ProductArticleNumber = Normalize(ProductArticleNumber, DefaultProductArticleNumber),
            ProductDescription = Normalize(ProductDescription, DefaultProductDescription),
            ProductQuantity = Normalize(ProductQuantity, DefaultProductQuantity),
            ProductWeight = Normalize(ProductWeight, DefaultProductWeight),
            ExcludedProductArticleNumbers = Normalize(ExcludedProductArticleNumbers, DefaultExcludedProductArticleNumbers),
            ExcludedProductDescriptions = Normalize(ExcludedProductDescriptions, DefaultExcludedProductDescriptions),
            DeliveryTypeFreiBordsteinkanteArticleNumbers = Normalize(DeliveryTypeFreiBordsteinkanteArticleNumbers, DefaultDeliveryTypeFreiBordsteinkanteArticleNumbers),
            DeliveryTypeMitVerteilungArticleNumbers = Normalize(DeliveryTypeMitVerteilungArticleNumbers, DefaultDeliveryTypeMitVerteilungArticleNumbers),
            DeliveryTypeMitVerteilungMontageArticleNumbers = Normalize(DeliveryTypeMitVerteilungMontageArticleNumbers, DefaultDeliveryTypeMitVerteilungMontageArticleNumbers),
            DeliveryTypeSpediteurArticleNumbers = Normalize(DeliveryTypeSpediteurArticleNumbers, DefaultDeliveryTypeSpediteurArticleNumbers),
            DeliveryTypePostArticleNumbers = Normalize(DeliveryTypePostArticleNumbers, DefaultDeliveryTypePostArticleNumbers),
            DeliveryTypeTresorBordsteinArticleNumbers = Normalize(DeliveryTypeTresorBordsteinArticleNumbers, DefaultDeliveryTypeTresorBordsteinArticleNumbers),
            DeliveryTypeTresorVerwendungArticleNumbers = Normalize(DeliveryTypeTresorVerwendungArticleNumbers, DefaultDeliveryTypeTresorVerwendungArticleNumbers),
            DeliveryTypeSelbstabholungArticleNumbers = Normalize(DeliveryTypeSelbstabholungArticleNumbers, DefaultDeliveryTypeSelbstabholungArticleNumbers)
        };

        if (effective.UsesLegacyDefaultStructure())
        {
            effective.AddressRecordElement = DefaultAddressRecordElement;
            effective.OrderRecordElement = DefaultOrderRecordElement;
            effective.ProductRecordElement = DefaultProductRecordElement;
            effective.OrderBillingAddressBlock = DefaultOrderBillingAddressBlock;
            effective.OrderDeliveryAddressBlock = DefaultOrderDeliveryAddressBlock;
            effective.OrderId = DefaultOrderId;
            effective.OrderNumber = DefaultOrderNumber;
            effective.OrderType = DefaultOrderType;
            effective.OrderDate = DefaultOrderDate;
            effective.OrderAddressId = DefaultOrderAddressId;
            effective.OrderDeliveryAddressId = DefaultOrderDeliveryAddressId;
            effective.OrderAddressNumber = DefaultOrderAddressNumber;
            effective.OrderDeliveryAddressNumber = DefaultOrderDeliveryAddressNumber;
            effective.OrderDeliveryCondition = DefaultOrderDeliveryCondition;
            effective.OrderDeliveryDate = DefaultOrderDeliveryDate;
            effective.OrderDeliveryCanOccurEarlier = DefaultOrderDeliveryCanOccurEarlier;
            effective.OrderDeliveryTime = DefaultOrderDeliveryTime;
            effective.OrderArchived = DefaultOrderArchived;
            effective.OrderLocked = DefaultOrderLocked;
            effective.OrderNote = DefaultOrderNote;
            effective.ProductOrderId = DefaultProductOrderId;
            effective.ProductArticleNumber = DefaultProductArticleNumber;
            effective.ProductDescription = DefaultProductDescription;
            effective.ProductQuantity = DefaultProductQuantity;
            effective.ProductWeight = DefaultProductWeight;
        }

        return effective;
    }

    private static string Normalize(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private bool UsesLegacyDefaultStructure()
    {
        return EqualsOrdinal(AddressRecordElement, LegacyAddressRecordElement) &&
               EqualsOrdinal(OrderRecordElement, LegacyOrderRecordElement) &&
               EqualsOrdinal(ProductRecordElement, LegacyProductRecordElement) &&
               EqualsOrdinal(OrderId, LegacyOrderId) &&
               EqualsOrdinal(OrderNumber, LegacyOrderNumber) &&
               EqualsOrdinal(OrderDeliveryCondition, LegacyOrderDeliveryCondition) &&
               EqualsOrdinal(ProductOrderId, LegacyProductOrderId) &&
               EqualsOrdinal(ProductArticleNumber, LegacyProductArticleNumber);
    }

    private static bool EqualsOrdinal(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }
}
