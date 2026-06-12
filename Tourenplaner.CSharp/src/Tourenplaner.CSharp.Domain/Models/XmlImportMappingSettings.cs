namespace Tourenplaner.CSharp.Domain.Models;

public sealed class XmlImportMappingSettings
{
    public const string DefaultAddressRecordElement = "AVE_Stamm";
    public const string DefaultOrderRecordElement = "WW_Kopf";
    public const string DefaultProductRecordElement = "WW_Pos";

    public const string DefaultAddressId = "Adresse";
    public const string DefaultAddressCompany = "Firma";
    public const string DefaultAddressLastName = "Nachname";
    public const string DefaultAddressFirstName = "Vorname";
    public const string DefaultAddressStreet = "Strasse";
    public const string DefaultAddressHouseNumber = "";
    public const string DefaultAddressPostalCode = "PLZ";
    public const string DefaultAddressCity = "Ort";
    public const string DefaultAddressCountry = "Land";
    public const string DefaultAddressEmail = "Email";
    public const string DefaultAddressPhone = "Telefon";
    public const string DefaultAddressContactPerson = "Kontaktperson";

    public const string DefaultOrderId = "Ident";
    public const string DefaultOrderNumber = "AuftragNr";
    public const string DefaultOrderType = "Typ";
    public const string DefaultOrderDate = "Datum";
    public const string DefaultOrderAddressId = "AdressID";
    public const string DefaultOrderDeliveryAddressId = "LieferadressID";
    public const string DefaultOrderDeliveryCondition = "LiefKondID";
    public const string DefaultOrderDeliveryDate = "Lieferdatum";
    public const string DefaultOrderArchived = "Archiviert";
    public const string DefaultOrderLocked = "";
    public const string DefaultOrderNote = "Notiz";

    public const string DefaultProductOrderId = "KopfID";
    public const string DefaultProductArticleNumber = "ArtikelID";
    public const string DefaultProductDescription = "Bezeichnung";
    public const string DefaultProductQuantity = "Menge";
    public const string DefaultProductWeight = "Gewicht";

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

    public string OrderId { get; set; } = DefaultOrderId;
    public string OrderNumber { get; set; } = DefaultOrderNumber;
    public string OrderType { get; set; } = DefaultOrderType;
    public string OrderDate { get; set; } = DefaultOrderDate;
    public string OrderAddressId { get; set; } = DefaultOrderAddressId;
    public string OrderDeliveryAddressId { get; set; } = DefaultOrderDeliveryAddressId;
    public string OrderDeliveryCondition { get; set; } = DefaultOrderDeliveryCondition;
    public string OrderDeliveryDate { get; set; } = DefaultOrderDeliveryDate;
    public string OrderArchived { get; set; } = DefaultOrderArchived;
    public string OrderLocked { get; set; } = DefaultOrderLocked;
    public string OrderNote { get; set; } = DefaultOrderNote;

    public string ProductOrderId { get; set; } = DefaultProductOrderId;
    public string ProductArticleNumber { get; set; } = DefaultProductArticleNumber;
    public string ProductDescription { get; set; } = DefaultProductDescription;
    public string ProductQuantity { get; set; } = DefaultProductQuantity;
    public string ProductWeight { get; set; } = DefaultProductWeight;

    public static XmlImportMappingSettings CreateDefault()
    {
        return new XmlImportMappingSettings();
    }

    public XmlImportMappingSettings WithDefaults()
    {
        return new XmlImportMappingSettings
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
            OrderId = Normalize(OrderId, DefaultOrderId),
            OrderNumber = Normalize(OrderNumber, DefaultOrderNumber),
            OrderType = Normalize(OrderType, DefaultOrderType),
            OrderDate = Normalize(OrderDate, DefaultOrderDate),
            OrderAddressId = Normalize(OrderAddressId, DefaultOrderAddressId),
            OrderDeliveryAddressId = Normalize(OrderDeliveryAddressId, DefaultOrderDeliveryAddressId),
            OrderDeliveryCondition = Normalize(OrderDeliveryCondition, DefaultOrderDeliveryCondition),
            OrderDeliveryDate = Normalize(OrderDeliveryDate, DefaultOrderDeliveryDate),
            OrderArchived = Normalize(OrderArchived, DefaultOrderArchived),
            OrderLocked = Normalize(OrderLocked, DefaultOrderLocked),
            OrderNote = Normalize(OrderNote, DefaultOrderNote),
            ProductOrderId = Normalize(ProductOrderId, DefaultProductOrderId),
            ProductArticleNumber = Normalize(ProductArticleNumber, DefaultProductArticleNumber),
            ProductDescription = Normalize(ProductDescription, DefaultProductDescription),
            ProductQuantity = Normalize(ProductQuantity, DefaultProductQuantity),
            ProductWeight = Normalize(ProductWeight, DefaultProductWeight)
        };
    }

    private static string Normalize(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }
}
