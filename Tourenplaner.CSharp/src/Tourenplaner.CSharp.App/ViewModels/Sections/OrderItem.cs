using System;
using System.Linq;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class OrderItem : ObservableObject
{
    private string _id = string.Empty;
    private string _customerName = string.Empty;
    private string _address = string.Empty;
    private string _scheduledDate = string.Empty;
    private string _assignedTourId = string.Empty;
    private string _latitude = string.Empty;
    private string _longitude = string.Empty;
    private string _orderAddressName = string.Empty;
    private string _orderAddressStreet = string.Empty;
    private string _orderAddressHouseNumber = string.Empty;
    private string _orderAddressPostalCode = string.Empty;
    private string _orderAddressCity = string.Empty;
    private string _deliveryName = string.Empty;
    private string _deliveryContactPerson = string.Empty;
    private string _deliveryStreet = string.Empty;
    private string _deliveryHouseNumber = string.Empty;
    private string _deliveryPostalCode = string.Empty;
    private string _deliveryCity = string.Empty;
    private string _email = string.Empty;
    private string _phone = string.Empty;
    private string _deliveryType = string.Empty;
    private string _orderStatus = string.Empty;
    private string _orderStatusBadgeBackground = "#E8F1FF";
    private string _orderStatusBadgeBorderBrush = "#BFDBFE";
    private string _orderStatusBadgeForeground = "#2563EB";
    private string _productsSummary = string.Empty;
    private string _notes = string.Empty;
    private bool _isArchived;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string CustomerName
    {
        get => _customerName;
        set => SetProperty(ref _customerName, value);
    }

    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public string ScheduledDate
    {
        get => _scheduledDate;
        set => SetProperty(ref _scheduledDate, value);
    }

    public string OrderAddressLine
    {
        get
        {
            var street = BuildStreetLine(OrderAddressStreet, OrderAddressHouseNumber);
            var postal = (OrderAddressPostalCode ?? string.Empty).Trim();
            var city = (OrderAddressCity ?? string.Empty).Trim();
            var postalCity = string.Join(' ', new[] { postal, city }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.Join(", ", new[] { street, postalCity }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }

    public string DeliveryStreetLine
    {
        get
        {
            var street = BuildStreetLine(DeliveryStreet, DeliveryHouseNumber);
            return string.IsNullOrWhiteSpace(street) ? (Address ?? string.Empty).Trim() : street;
        }
    }

    public string DeliveryPostalCityLine
    {
        get
        {
            var postal = (DeliveryPostalCode ?? string.Empty).Trim();
            var city = (DeliveryCity ?? string.Empty).Trim();
            return string.Join(' ', new[] { postal, city }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }

    public string DeliveryPersonPrimary
    {
        get
        {
            var contact = (DeliveryContactPerson ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(contact))
            {
                return contact;
            }

            return (DeliveryName ?? string.Empty).Trim();
        }
    }

    public string DeliveryPersonSecondary
    {
        get
        {
            var phone = (Phone ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(phone))
            {
                return string.Empty;
            }

            var primary = DeliveryPersonPrimary;
            return string.Equals(primary, phone, StringComparison.OrdinalIgnoreCase) ? string.Empty : phone;
        }
    }

    public string AssignedTourId
    {
        get => _assignedTourId;
        set => SetProperty(ref _assignedTourId, value);
    }

    public string Latitude
    {
        get => _latitude;
        set => SetProperty(ref _latitude, value);
    }

    public string Longitude
    {
        get => _longitude;
        set => SetProperty(ref _longitude, value);
    }

    public string OrderAddressName
    {
        get => _orderAddressName;
        set => SetProperty(ref _orderAddressName, value);
    }

    public string OrderAddressStreet
    {
        get => _orderAddressStreet;
        set => SetProperty(ref _orderAddressStreet, value);
    }

    public string OrderAddressHouseNumber
    {
        get => _orderAddressHouseNumber;
        set => SetProperty(ref _orderAddressHouseNumber, value);
    }

    public string OrderAddressPostalCode
    {
        get => _orderAddressPostalCode;
        set => SetProperty(ref _orderAddressPostalCode, value);
    }

    public string OrderAddressCity
    {
        get => _orderAddressCity;
        set => SetProperty(ref _orderAddressCity, value);
    }

    public string DeliveryName
    {
        get => _deliveryName;
        set => SetProperty(ref _deliveryName, value);
    }

    public string DeliveryContactPerson
    {
        get => _deliveryContactPerson;
        set => SetProperty(ref _deliveryContactPerson, value);
    }

    public string DeliveryStreet
    {
        get => _deliveryStreet;
        set => SetProperty(ref _deliveryStreet, value);
    }

    public string DeliveryHouseNumber
    {
        get => _deliveryHouseNumber;
        set => SetProperty(ref _deliveryHouseNumber, value);
    }

    public string DeliveryPostalCode
    {
        get => _deliveryPostalCode;
        set => SetProperty(ref _deliveryPostalCode, value);
    }

    public string DeliveryCity
    {
        get => _deliveryCity;
        set => SetProperty(ref _deliveryCity, value);
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string Phone
    {
        get => _phone;
        set => SetProperty(ref _phone, value);
    }

    public string DeliveryType
    {
        get => _deliveryType;
        set => SetProperty(ref _deliveryType, value);
    }

    public string OrderStatus
    {
        get => _orderStatus;
        set => SetProperty(ref _orderStatus, value);
    }

    public string OrderStatusBadgeBackground
    {
        get => _orderStatusBadgeBackground;
        set => SetProperty(ref _orderStatusBadgeBackground, value);
    }

    public string OrderStatusBadgeBorderBrush
    {
        get => _orderStatusBadgeBorderBrush;
        set => SetProperty(ref _orderStatusBadgeBorderBrush, value);
    }

    public string OrderStatusBadgeForeground
    {
        get => _orderStatusBadgeForeground;
        set => SetProperty(ref _orderStatusBadgeForeground, value);
    }

    public string ProductsSummary
    {
        get => _productsSummary;
        set => SetProperty(ref _productsSummary, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool IsArchived
    {
        get => _isArchived;
        set => SetProperty(ref _isArchived, value);
    }

    private static string BuildStreetLine(string? street, string? houseNumber)
    {
        return string.Join(" ", new[]
        {
            (street ?? string.Empty).Trim(),
            (houseNumber ?? string.Empty).Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}

internal readonly record struct OrderStatusPalette(string BackgroundHex, string BorderHex, string ForegroundHex);

internal static class OrderStatusDisplayPalette
{
    private static readonly OrderStatusPalette BluePalette = new("#E8F1FF", "#BFDBFE", "#2563EB");
    private static readonly OrderStatusPalette GreenPalette = new("#ECFDF3", "#BBF7D0", "#16A34A");
    private static readonly OrderStatusPalette PurplePalette = new("#F3E8FF", "#D8B4FE", "#9333EA");
    private static readonly OrderStatusPalette OrangePalette = new("#FFF3E6", "#FDBA74", "#EA580C");
    private static readonly OrderStatusPalette AmberPalette = new("#FFF7ED", "#FCD34D", "#D97706");

    public static OrderStatusPalette Resolve(Order order)
    {
        var normalizedStatus = Order.NormalizeOrderStatus(order.OrderStatus);
        if (string.Equals(normalizedStatus, Order.PartiallyPendingPreparationStatus, StringComparison.OrdinalIgnoreCase))
        {
            var baseStatus = Order.ResolvePartiallyPendingPreparationBaseStatus(order.Products);
            return Resolve(baseStatus);
        }

        return Resolve(normalizedStatus);
    }

    public static OrderStatusPalette Resolve(string? orderStatus)
    {
        var normalizedStatus = Order.NormalizeOrderStatus(orderStatus);
        if (string.Equals(normalizedStatus, Order.PartiallyReadyStatus, StringComparison.OrdinalIgnoreCase))
        {
            return GreenPalette;
        }

        if (string.Equals(normalizedStatus, Order.ReadyToDeliverStatus, StringComparison.OrdinalIgnoreCase))
        {
            return PurplePalette;
        }

        if (string.Equals(normalizedStatus, Order.InTransitStatus, StringComparison.OrdinalIgnoreCase))
        {
            return OrangePalette;
        }

        if (string.Equals(normalizedStatus, Order.PendingPreparationStatus, StringComparison.OrdinalIgnoreCase))
        {
            return GreenPalette;
        }

        if (string.Equals(normalizedStatus, Order.PartiallyInTransitStatus, StringComparison.OrdinalIgnoreCase))
        {
            return AmberPalette;
        }

        return BluePalette;
    }
}
