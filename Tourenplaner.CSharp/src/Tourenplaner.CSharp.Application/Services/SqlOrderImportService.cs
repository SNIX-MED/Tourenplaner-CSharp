using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Application.Abstractions;

namespace Tourenplaner.CSharp.Application.Services;

public class ImportResult
{
    public int CreatedOrders { get; set; }
    public int UpdatedOrders { get; set; }
    public List<string> Errors { get; set; } = new();
    public DateTime ImportedAt { get; set; }
}

public interface ISqlOrderImportService
{
    Task<ImportResult> ImportOrdersAsync(
        List<SqlOrderImportData> sqlOrders,
        IOrderRepository orderRepository,
        ISettingsRepository settingsRepository);
}

public class SqlOrderImportService : ISqlOrderImportService
{
    public async Task<ImportResult> ImportOrdersAsync(
        List<SqlOrderImportData> sqlOrders,
        IOrderRepository orderRepository,
        ISettingsRepository settingsRepository)
    {
        var result = new ImportResult { ImportedAt = DateTime.Now };
        var existingOrders = (await orderRepository.GetAllAsync()).ToList();
        var settings = await settingsRepository.GetAsync();

        foreach (var sqlOrder in sqlOrders)
        {
            try
            {
                // Liefermethode bestimmen
                var deliveryMethod = DeliveryMethodExtensions.ParseDeliveryMethod(sqlOrder.Lieferbedingung);
                var isMapOrder = deliveryMethod.IsMapOrder();
                
                // Bestehenden Auftrag suchen
                var existingOrder = existingOrders
                    .FirstOrDefault(o => o.Id == sqlOrder.AuftragNr);

                if (existingOrder != null)
                {
                    // Auftrag aktualisieren
                    UpdateOrder(existingOrder, sqlOrder, isMapOrder);
                    result.UpdatedOrders++;
                }
                else
                {
                    // Neuen Auftrag erstellen
                    var newOrder = CreateOrder(sqlOrder, isMapOrder);
                    existingOrders.Add(newOrder);
                    result.CreatedOrders++;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(
                    $"Fehler bei Auftrag {sqlOrder.AuftragNr}: {ex.Message}");
            }
        }

        // Alle Aufträge speichern
        if (result.CreatedOrders > 0 || result.UpdatedOrders > 0)
        {
            await orderRepository.SaveAllAsync(existingOrders);
            settings.LastSqlImportDate = DateTime.Now;
            await settingsRepository.SaveAsync(settings);
        }

        return result;
    }

    private Order CreateOrder(
        SqlOrderImportData sqlOrder,
        bool isMapOrder)
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

        var order = new Order
        {
            Id = sqlOrder.AuftragNr,
            CustomerName = BuildCustomerName(sqlOrder),
            ScheduledDate = DateOnly.FromDateTime(sqlOrder.AuftragsDatum),
            Type = isMapOrder ? OrderType.Map : OrderType.NonMap,
            
            // Auftragsadresse
            OrderAddress = new OrderAddressInfo
            {
                Name = sqlOrder.KundeFirma,
                Street = auftragsAdresse,
                PostalCode = sqlOrder.KundePLZ,
                City = sqlOrder.KundeOrt
            },
            
            // Lieferadresse
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
            
            // Produkte
            Products = sqlOrder.Produkte.Select((p, idx) => new OrderProductInfo
            {
                Name = p.Bezeichnung,
                Quantity = (int)p.Menge,
                UnitWeightKg = (double)p.Gewicht,
                WeightKg = (double)(p.Bruttogewicht * p.Menge),
                Dimensions = string.Empty
            }).ToList(),
            
            DeliveryType = DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(sqlOrder.Lieferbedingung),
            OrderStatus = "nicht festgelegt",
            Notes = sqlOrder.Notiz
        };

        return order;
    }

    private void UpdateOrder(
        Order existingOrder,
        SqlOrderImportData sqlOrder,
        bool isMapOrder)
    {
        existingOrder.Type = isMapOrder ? OrderType.Map : OrderType.NonMap;
        existingOrder.DeliveryType = DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(sqlOrder.Lieferbedingung);
        existingOrder.Notes = sqlOrder.Notiz;
        existingOrder.ScheduledDate = DateOnly.FromDateTime(sqlOrder.AuftragsDatum);
        existingOrder.CustomerName = BuildCustomerName(sqlOrder);
        existingOrder.Email = ResolvePreferredContact(sqlOrder.LieferEmail, sqlOrder.KundeEmail);
        existingOrder.Phone = ResolvePreferredContact(sqlOrder.LieferTelefon, sqlOrder.KundeTelefon);

        var auftragsAdresse = BuildAddress(
            sqlOrder.KundeStrasse,
            sqlOrder.KundeHausnummer,
            sqlOrder.KundePLZ,
            sqlOrder.KundeOrt,
            sqlOrder.KundeLand);
        existingOrder.OrderAddress = new OrderAddressInfo
        {
            Name = sqlOrder.KundeFirma,
            Street = auftragsAdresse,
            PostalCode = sqlOrder.KundePLZ,
            City = sqlOrder.KundeOrt
        };

        // Keep the legacy flat address string in sync for existing consumers.
        existingOrder.Address = $"{auftragsAdresse}, {sqlOrder.KundePLZ} {sqlOrder.KundeOrt}".Trim(' ', ',');

        var deliveryAddress = ResolveDeliveryAddress(sqlOrder);

        // Produkte aktualisieren
        existingOrder.Products = sqlOrder.Produkte.Select((p, idx) => new OrderProductInfo
        {
            Name = p.Bezeichnung,
            Quantity = (int)p.Menge,
            UnitWeightKg = (double)p.Gewicht,
            WeightKg = (double)(p.Bruttogewicht * p.Menge),
            Dimensions = string.Empty
        }).ToList();

        // Lieferadresse aktualisieren
        existingOrder.DeliveryAddress = new DeliveryAddressInfo
        {
            Name = deliveryAddress.Name,
            ContactPerson = deliveryAddress.ContactPerson,
            Street = deliveryAddress.Street,
            PostalCode = deliveryAddress.PostalCode,
            City = deliveryAddress.City
        };
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
            parts.Add(sqlOrder.KundeFirma);
        if (!string.IsNullOrWhiteSpace(sqlOrder.KundeNachname))
            parts.Add(sqlOrder.KundeNachname);
        if (!string.IsNullOrWhiteSpace(sqlOrder.KundeVorname))
            parts.Add(sqlOrder.KundeVorname);

        return string.Join(" ", parts).Trim();
    }

    private string BuildContactPersonName(SqlOrderImportData sqlOrder)
    {
        var parts = new List<string>();
        
        // Lieferadresse hat Vorrang
        if (!string.IsNullOrWhiteSpace(sqlOrder.LieferVorname))
            parts.Add(sqlOrder.LieferVorname);
        if (!string.IsNullOrWhiteSpace(sqlOrder.LieferNachname))
            parts.Add(sqlOrder.LieferNachname);

        // Fallback auf Kundenadresse
        if (parts.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(sqlOrder.KundeVorname))
                parts.Add(sqlOrder.KundeVorname);
            if (!string.IsNullOrWhiteSpace(sqlOrder.KundeNachname))
                parts.Add(sqlOrder.KundeNachname);
        }

        return string.Join(" ", parts).Trim();
    }

    private string BuildAddress(string street, string number, string zip, string city, string country)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(street))
            parts.Add(street);
        if (!string.IsNullOrWhiteSpace(number))
            parts.Add(number);

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
}
