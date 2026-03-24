using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class ManualOrderDialogWindow : Window
{
    public ManualOrderDialogWindow(Order? existingOrder = null)
    {
        InitializeComponent();
        ViewModel = new ManualOrderDialogViewModel(existingOrder);
        DataContext = ViewModel;
        Title = existingOrder is null ? "Kundenkartei" : $"Auftrag bearbeiten - {existingOrder.Id}";
    }

    public ManualOrderDialogViewModel ViewModel { get; }

    public Order? CreatedOrder { get; private set; }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildOrder(out var order, out var validationError))
        {
            MessageBox.Show(
                this,
                validationError,
                "Eingabe pruefen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        CreatedOrder = order;
        DialogResult = true;
        Close();
    }
}

public sealed class ManualOrderDialogViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<string> DeliveryTypes =
    [
        "Frei Bordsteinkante",
        "Mit Verteilung",
        "Mit Verteilung & Montage"
    ];

    private static readonly IReadOnlyList<string> Statuses =
    [
        "nicht festgelegt",
        "Bestellt",
        "Auf dem Weg",
        "An Lager"
    ];

    private ProductLineInput? _selectedProductLine;
    private string _orderNumber = string.Empty;
    private string _orderDateText = DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    private string _orderAddressName = string.Empty;
    private string _orderAddressStreet = string.Empty;
    private string _orderAddressPostalCode = string.Empty;
    private string _orderAddressCity = string.Empty;
    private string _deliveryName = string.Empty;
    private string _deliveryContactPerson = string.Empty;
    private string _deliveryStreet = string.Empty;
    private string _deliveryPostalCode = string.Empty;
    private string _deliveryCity = string.Empty;
    private string _email = string.Empty;
    private string _phone = string.Empty;
    private string _selectedDeliveryType = DeliveryTypes[0];
    private string _selectedStatus = Statuses[0];
    private string _notes = string.Empty;

    private GeoPoint? _existingLocation;
    private string? _existingAssignedTourId;

    public ManualOrderDialogViewModel(Order? existingOrder = null)
    {
        AddProductLineCommand = new DelegateCommand(AddProductLine);
        RemoveProductLineCommand = new DelegateCommand(RemoveSelectedProductLine, () => SelectedProductLine is not null);
        ApplyExistingOrder(existingOrder);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProductLineInput> ProductLines { get; } = new();

    public ICommand AddProductLineCommand { get; }

    public ICommand RemoveProductLineCommand { get; }

    public IReadOnlyList<string> DeliveryTypeOptions => DeliveryTypes;

    public IReadOnlyList<string> StatusOptions => Statuses;

    public ProductLineInput? SelectedProductLine
    {
        get => _selectedProductLine;
        set
        {
            if (SetProperty(ref _selectedProductLine, value) && RemoveProductLineCommand is DelegateCommand remove)
            {
                remove.RaiseCanExecuteChanged();
            }
        }
    }

    public string OrderNumber
    {
        get => _orderNumber;
        set => SetProperty(ref _orderNumber, value);
    }

    public string OrderDateText
    {
        get => _orderDateText;
        set => SetProperty(ref _orderDateText, value);
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

    public string SelectedDeliveryType
    {
        get => _selectedDeliveryType;
        set => SetProperty(ref _selectedDeliveryType, value);
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set => SetProperty(ref _selectedStatus, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool TryBuildOrder(out Order? order, out string error)
    {
        order = null;
        error = string.Empty;

        var id = (OrderNumber ?? string.Empty).Trim();
        var deliveryName = (DeliveryName ?? string.Empty).Trim();
        var deliveryStreet = (DeliveryStreet ?? string.Empty).Trim();
        var deliveryPostalCode = (DeliveryPostalCode ?? string.Empty).Trim();
        var deliveryCity = (DeliveryCity ?? string.Empty).Trim();
        var normalizedOrderDateText = (OrderDateText ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(deliveryName) ||
            string.IsNullOrWhiteSpace(deliveryStreet) ||
            string.IsNullOrWhiteSpace(deliveryPostalCode) ||
            string.IsNullOrWhiteSpace(deliveryCity))
        {
            error = "Bitte mindestens Auftragsnummer, Liefername und Lieferadresse ausfuellen.";
            return false;
        }

        if (!DateTime.TryParseExact(
                normalizedOrderDateText,
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedOrderDate))
        {
            error = "Bitte ein gueltiges Auftragsdatum im Format DD.MM.YYYY eingeben.";
            return false;
        }

        var products = ProductLines
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new OrderProductInfo
            {
                Name = x.Name.Trim(),
                WeightKg = ParseWeight(x.WeightKgText)
            })
            .ToList();

        order = new Order
        {
            Id = id,
            ScheduledDate = DateOnly.FromDateTime(parsedOrderDate),
            Type = OrderType.Map,
            CustomerName = deliveryName,
            Address = $"{deliveryStreet}, {deliveryPostalCode} {deliveryCity}",
            OrderAddress = new OrderAddressInfo
            {
                Name = (OrderAddressName ?? string.Empty).Trim(),
                Street = (OrderAddressStreet ?? string.Empty).Trim(),
                PostalCode = (OrderAddressPostalCode ?? string.Empty).Trim(),
                City = (OrderAddressCity ?? string.Empty).Trim()
            },
            DeliveryAddress = new DeliveryAddressInfo
            {
                Name = deliveryName,
                ContactPerson = (DeliveryContactPerson ?? string.Empty).Trim(),
                Street = deliveryStreet,
                PostalCode = deliveryPostalCode,
                City = deliveryCity
            },
            Email = (Email ?? string.Empty).Trim(),
            Phone = (Phone ?? string.Empty).Trim(),
            Products = products,
            DeliveryType = (SelectedDeliveryType ?? DeliveryTypes[0]).Trim(),
            OrderStatus = string.IsNullOrWhiteSpace(SelectedStatus)
                ? Statuses[0]
                : SelectedStatus.Trim(),
            Notes = (Notes ?? string.Empty).Trim(),
            AssignedTourId = _existingAssignedTourId,
            Location = _existingLocation
        };

        return true;
    }

    private void ApplyExistingOrder(Order? existingOrder)
    {
        if (existingOrder is null)
        {
            ProductLines.Add(new ProductLineInput());
            return;
        }

        _existingLocation = existingOrder.Location;
        _existingAssignedTourId = existingOrder.AssignedTourId;
        OrderNumber = existingOrder.Id;
        OrderDateText = existingOrder.ScheduledDate.ToDateTime(TimeOnly.MinValue).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        OrderAddressName = existingOrder.OrderAddress?.Name ?? string.Empty;
        OrderAddressStreet = existingOrder.OrderAddress?.Street ?? string.Empty;
        OrderAddressPostalCode = existingOrder.OrderAddress?.PostalCode ?? string.Empty;
        OrderAddressCity = existingOrder.OrderAddress?.City ?? string.Empty;
        DeliveryName = existingOrder.DeliveryAddress?.Name ?? existingOrder.CustomerName ?? string.Empty;
        DeliveryContactPerson = existingOrder.DeliveryAddress?.ContactPerson ?? string.Empty;
        DeliveryStreet = existingOrder.DeliveryAddress?.Street ?? string.Empty;
        DeliveryPostalCode = existingOrder.DeliveryAddress?.PostalCode ?? string.Empty;
        DeliveryCity = existingOrder.DeliveryAddress?.City ?? string.Empty;
        Email = existingOrder.Email ?? string.Empty;
        Phone = existingOrder.Phone ?? string.Empty;
        SelectedDeliveryType = string.IsNullOrWhiteSpace(existingOrder.DeliveryType)
            ? DeliveryTypes[0]
            : existingOrder.DeliveryType;
        SelectedStatus = string.IsNullOrWhiteSpace(existingOrder.OrderStatus) ||
                         string.Equals(existingOrder.OrderStatus, "Bereits eingeplant", StringComparison.OrdinalIgnoreCase)
            ? Statuses[0]
            : existingOrder.OrderStatus;
        Notes = existingOrder.Notes ?? string.Empty;

        ProductLines.Clear();
        foreach (var product in existingOrder.Products ?? [])
        {
            ProductLines.Add(new ProductLineInput
            {
                Name = product.Name ?? string.Empty,
                WeightKgText = product.WeightKg.ToString("0.###", CultureInfo.InvariantCulture)
            });
        }

        if (ProductLines.Count == 0)
        {
            ProductLines.Add(new ProductLineInput());
        }
    }

    private void AddProductLine()
    {
        var line = new ProductLineInput();
        ProductLines.Add(line);
        SelectedProductLine = line;
    }

    private void RemoveSelectedProductLine()
    {
        if (SelectedProductLine is null)
        {
            return;
        }

        ProductLines.Remove(SelectedProductLine);
        if (ProductLines.Count == 0)
        {
            ProductLines.Add(new ProductLineInput());
        }

        SelectedProductLine = ProductLines.FirstOrDefault();
    }

    private static double ParseWeight(string? text)
    {
        var value = (text ?? string.Empty).Trim().Replace(",", ".", StringComparison.Ordinal);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, parsed)
            : 0d;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class ProductLineInput : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _weightKgText = "0";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string WeightKgText
    {
        get => _weightKgText;
        set => SetProperty(ref _weightKgText, value);
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
