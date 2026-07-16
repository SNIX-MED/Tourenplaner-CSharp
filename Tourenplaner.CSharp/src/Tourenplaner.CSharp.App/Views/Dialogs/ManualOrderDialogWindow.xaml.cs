using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class ManualOrderDialogWindow : Window
{
    private readonly bool _isEditMode;

    public ManualOrderDialogWindow(
        Order? existingOrder = null,
        IReadOnlyList<string>? deliveryTypes = null,
        OrderType defaultOrderType = OrderType.Map)
    {
        InitializeComponent();
        _isEditMode = existingOrder is not null;
        ViewModel = new ManualOrderDialogViewModel(existingOrder, deliveryTypes, defaultOrderType);
        DataContext = ViewModel;
        Title = existingOrder is null ? "Kundenkartei" : $"Auftrag bearbeiten - {existingOrder.Id}";
        DeleteButton.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;
    }

    public ManualOrderDialogViewModel ViewModel { get; }

    public Order? CreatedOrder { get; private set; }

    public bool DeleteRequested { get; private set; }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        DeleteRequested = true;
        DialogResult = false;
        Close();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildOrder(out var order, out var validationError))
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(
                this,
                validationError,
                "Eingabe prüfen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DeleteRequested = false;
        CreatedOrder = order;
        DialogResult = true;
        Close();
    }

    private void OnAddProductClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OrderProductDialogWindow
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            ViewModel.AddProductLine(dialog.Result);
        }
    }

    private void OnEditProductClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProductLine is null)
        {
            return;
        }

        var dialog = new OrderProductDialogWindow(ViewModel.SelectedProductLine)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            ViewModel.UpdateSelectedProductLine(dialog.Result);
        }
    }

    private void OnRemoveProductClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.RemoveSelectedProductLine();
    }

    private void OnProductGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedProductLine is null)
        {
            return;
        }

        OnEditProductClicked(sender, new RoutedEventArgs());
    }

    private void OnProductGridPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ContentScrollViewer is null)
        {
            return;
        }

        ContentScrollViewer.ScrollToVerticalOffset(ContentScrollViewer.VerticalOffset - (e.Delta / 3d));
        e.Handled = true;
    }

    private void OnUseOrderAddressForDeliveryClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.CopyOrderAddressToDeliveryAddress();
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 3)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var textBox = FindAncestor<TextBox>(source);
        if (textBox is null)
        {
            return;
        }

        textBox.Focus();
        textBox.SelectAll();
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T typed)
            {
                return typed;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}

public sealed class ManualOrderDialogViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<string> Statuses =
    [
        Order.DefaultOrderStatus,
        Order.OrderedStatus,
        Order.InTransitStatus,
        Order.PartiallyInTransitStatus,
        Order.PendingPreparationStatus,
        Order.PartiallyPendingPreparationStatus,
        Order.PartiallyReadyStatus,
        Order.ReadyToDeliverStatus
    ];

    private readonly IReadOnlyList<string> _deliveryTypes;
    private readonly OrderType _defaultOrderType;
    private ProductLineInput? _selectedProductLine;
    private string _orderNumber = string.Empty;
    private string _orderDateText = DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    private string _orderAddressName = string.Empty;
    private string _orderAddressContactPerson = string.Empty;
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
    private string _selectedDeliveryType = string.Empty;
    private string _selectedStatus = Statuses[0];
    private string _notes = string.Empty;
    private bool _istVorauszahlung;
    private bool _isArchived;

    private GeoPoint? _existingLocation;
    private string? _existingAssignedTourId;

    public ManualOrderDialogViewModel(
        Order? existingOrder = null,
        IReadOnlyList<string>? deliveryTypes = null,
        OrderType defaultOrderType = OrderType.Map)
    {
        _deliveryTypes = (deliveryTypes ?? DeliveryMethodExtensions.MapDeliveryTypeOptions)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_deliveryTypes.Count == 0)
        {
            _deliveryTypes = DeliveryMethodExtensions.MapDeliveryTypeOptions;
        }

        _defaultOrderType = defaultOrderType;
        _selectedDeliveryType = _deliveryTypes[0];
        ApplyExistingOrder(existingOrder);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProductLineInput> ProductLines { get; } = new();

    public IReadOnlyList<string> DeliveryTypeOptions => _deliveryTypes;

    public IReadOnlyList<string> StatusOptions => Statuses;

    public ProductLineInput? SelectedProductLine
    {
        get => _selectedProductLine;
        set
        {
            if (SetProperty(ref _selectedProductLine, value))
            {
                OnPropertyChanged(nameof(CanEditOrRemoveProductLine));
            }
        }
    }

    public bool CanEditOrRemoveProductLine => SelectedProductLine is not null;

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

    public string OrderAddressContactPerson
    {
        get => _orderAddressContactPerson;
        set => SetProperty(ref _orderAddressContactPerson, value);
    }

    public string OrderAddressPostalCode
    {
        get => _orderAddressPostalCode;
        set => SetProperty(ref _orderAddressPostalCode, value);
    }

    public string OrderAddressHouseNumber
    {
        get => _orderAddressHouseNumber;
        set => SetProperty(ref _orderAddressHouseNumber, value);
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

    public string SelectedDeliveryType
    {
        get => _selectedDeliveryType;
        set => SetProperty(ref _selectedDeliveryType, value);
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetProperty(ref _selectedStatus, value))
            {
                OnPropertyChanged(nameof(SelectedStatusBadgeBackground));
                OnPropertyChanged(nameof(SelectedStatusBadgeBorderBrush));
                OnPropertyChanged(nameof(SelectedStatusBadgeForeground));
            }
        }
    }

    public string SelectedStatusBadgeBackground => ResolveSelectedStatusPalette().BackgroundHex;

    public string SelectedStatusBadgeBorderBrush => ResolveSelectedStatusPalette().BorderHex;

    public string SelectedStatusBadgeForeground => ResolveSelectedStatusPalette().ForegroundHex;

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool IstVorauszahlung
    {
        get => _istVorauszahlung;
        set => SetProperty(ref _istVorauszahlung, value);
    }

    public bool IsArchived
    {
        get => _isArchived;
        set
        {
            if (SetProperty(ref _isArchived, value))
            {
                OnPropertyChanged(nameof(ArchiveToggleText));
            }
        }
    }

    public string ArchiveToggleText => IsArchived ? "Archiviert" : "Auftrag archivieren";

    public bool TryBuildOrder(out Order? order, out string error)
    {
        order = null;
        error = string.Empty;

        var id = (OrderNumber ?? string.Empty).Trim();
        var deliveryName = (DeliveryName ?? string.Empty).Trim();
        var deliveryStreet = (DeliveryStreet ?? string.Empty).Trim();
        var deliveryHouseNumber = (DeliveryHouseNumber ?? string.Empty).Trim();
        var deliveryPostalCode = (DeliveryPostalCode ?? string.Empty).Trim();
        var deliveryCity = (DeliveryCity ?? string.Empty).Trim();
        var normalizedOrderDateText = (OrderDateText ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(deliveryName) ||
            string.IsNullOrWhiteSpace(deliveryStreet) ||
            string.IsNullOrWhiteSpace(deliveryPostalCode) ||
            string.IsNullOrWhiteSpace(deliveryCity))
        {
            error = "Bitte mindestens Auftragsnummer, Liefername und Lieferadresse ausfüllen.";
            return false;
        }

        if (!DateTime.TryParseExact(
                normalizedOrderDateText,
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedOrderDate))
        {
            error = "Bitte ein gültiges Auftragsdatum im Format DD.MM.YYYY eingeben.";
            return false;
        }

        var deliveryStreetLine = BuildStreetLine(deliveryStreet, deliveryHouseNumber);

        var products = ProductLines
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => x.ToOrderProductInfo())
            .ToList();

        order = new Order
        {
            Id = id,
            ScheduledDate = DateOnly.FromDateTime(parsedOrderDate),
            Type = _defaultOrderType,
            CustomerName = deliveryName,
            Address = $"{deliveryStreetLine}, {deliveryPostalCode} {deliveryCity}",
            OrderAddress = new OrderAddressInfo
            {
                Name = (OrderAddressName ?? string.Empty).Trim(),
                ContactPerson = (OrderAddressContactPerson ?? string.Empty).Trim(),
                Street = (OrderAddressStreet ?? string.Empty).Trim(),
                HouseNumber = (OrderAddressHouseNumber ?? string.Empty).Trim(),
                PostalCode = (OrderAddressPostalCode ?? string.Empty).Trim(),
                City = (OrderAddressCity ?? string.Empty).Trim()
            },
            DeliveryAddress = new DeliveryAddressInfo
            {
                Name = deliveryName,
                ContactPerson = (DeliveryContactPerson ?? string.Empty).Trim(),
                Street = deliveryStreet,
                HouseNumber = deliveryHouseNumber,
                PostalCode = deliveryPostalCode,
                City = deliveryCity
            },
            Email = (Email ?? string.Empty).Trim(),
            Phone = (Phone ?? string.Empty).Trim(),
            Products = products,
            DeliveryType = DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(
                (SelectedDeliveryType ?? _deliveryTypes[0]).Trim()),
            OrderStatus = Order.ResolveOrderStatusFromProducts(products),
            IstVorauszahlung = IstVorauszahlung,
            Notes = (Notes ?? string.Empty).Trim(),
            AssignedTourId = _existingAssignedTourId,
            Location = _existingLocation,
            IsArchived = IsArchived
        };

        return true;
    }

    public void AddProductLine(ProductLineInput line)
    {
        ProductLines.Add(line);
        SelectedProductLine = line;
        RefreshSelectedStatusFromProducts();
    }

    public void UpdateSelectedProductLine(ProductLineInput line)
    {
        if (SelectedProductLine is null)
        {
            return;
        }

        var index = ProductLines.IndexOf(SelectedProductLine);
        if (index < 0)
        {
            return;
        }

        ProductLines[index] = line;
        SelectedProductLine = line;
        RefreshSelectedStatusFromProducts();
    }

    public void RemoveSelectedProductLine()
    {
        if (SelectedProductLine is null)
        {
            return;
        }

        var index = ProductLines.IndexOf(SelectedProductLine);
        ProductLines.Remove(SelectedProductLine);
        SelectedProductLine = ProductLines.ElementAtOrDefault(Math.Max(0, index - 1)) ?? ProductLines.FirstOrDefault();
        RefreshSelectedStatusFromProducts();
    }

    public void CopyOrderAddressToDeliveryAddress()
    {
        DeliveryName = (OrderAddressName ?? string.Empty).Trim();
        DeliveryContactPerson = (OrderAddressContactPerson ?? string.Empty).Trim();
        DeliveryStreet = (OrderAddressStreet ?? string.Empty).Trim();
        DeliveryHouseNumber = (OrderAddressHouseNumber ?? string.Empty).Trim();
        DeliveryPostalCode = (OrderAddressPostalCode ?? string.Empty).Trim();
        DeliveryCity = (OrderAddressCity ?? string.Empty).Trim();
    }

    private void ApplyExistingOrder(Order? existingOrder)
    {
        if (existingOrder is null)
        {
            return;
        }

        _existingLocation = existingOrder.Location;
        _existingAssignedTourId = existingOrder.AssignedTourId;
        OrderNumber = existingOrder.Id;
        OrderDateText = existingOrder.ScheduledDate.ToDateTime(TimeOnly.MinValue).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        OrderAddressName = existingOrder.OrderAddress?.Name ?? string.Empty;
        OrderAddressContactPerson = existingOrder.OrderAddress?.ContactPerson ?? string.Empty;
        OrderAddressStreet = existingOrder.OrderAddress?.Street ?? string.Empty;
        OrderAddressHouseNumber = string.IsNullOrWhiteSpace(existingOrder.OrderAddress?.HouseNumber)
            ? existingOrder.OrderAddress?.Additional ?? string.Empty
            : existingOrder.OrderAddress.HouseNumber;
        OrderAddressPostalCode = existingOrder.OrderAddress?.PostalCode ?? string.Empty;
        OrderAddressCity = existingOrder.OrderAddress?.City ?? string.Empty;
        DeliveryName = existingOrder.DeliveryAddress?.Name ?? existingOrder.CustomerName ?? string.Empty;
        DeliveryContactPerson = existingOrder.DeliveryAddress?.ContactPerson ?? string.Empty;
        DeliveryStreet = existingOrder.DeliveryAddress?.Street ?? string.Empty;
        DeliveryHouseNumber = string.IsNullOrWhiteSpace(existingOrder.DeliveryAddress?.HouseNumber)
            ? existingOrder.DeliveryAddress?.Additional ?? string.Empty
            : existingOrder.DeliveryAddress.HouseNumber;
        DeliveryPostalCode = existingOrder.DeliveryAddress?.PostalCode ?? string.Empty;
        DeliveryCity = existingOrder.DeliveryAddress?.City ?? string.Empty;
        Email = existingOrder.Email ?? string.Empty;
        Phone = existingOrder.Phone ?? string.Empty;
        var normalizedDeliveryType = DeliveryMethodExtensions.NormalizeDeliveryTypeLabel(existingOrder.DeliveryType);
        SelectedDeliveryType = _deliveryTypes.Any(x => string.Equals(x, normalizedDeliveryType, StringComparison.OrdinalIgnoreCase))
            ? _deliveryTypes.First(x => string.Equals(x, normalizedDeliveryType, StringComparison.OrdinalIgnoreCase))
            : _deliveryTypes[0];
        Notes = existingOrder.Notes ?? string.Empty;
        IstVorauszahlung = existingOrder.IstVorauszahlung;
        IsArchived = existingOrder.IsArchived;

        ProductLines.Clear();
        foreach (var product in existingOrder.Products ?? [])
        {
            ProductLines.Add(ProductLineInput.FromOrderProductInfo(product));
        }

        RefreshSelectedStatusFromProducts();
        SelectedProductLine = ProductLines.FirstOrDefault();
    }

    private void RefreshSelectedStatusFromProducts()
    {
        var products = ProductLines
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => x.ToOrderProductInfo());

        SelectedStatus = Order.ResolveOrderStatusFromProducts(products);
    }

    private OrderStatusPalette ResolveSelectedStatusPalette()
    {
        var products = ProductLines
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => x.ToOrderProductInfo())
            .ToList();

        var order = new Order
        {
            OrderStatus = SelectedStatus,
            Products = products
        };

        return OrderStatusDisplayPalette.Resolve(order);
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

public sealed class ProductLineInput : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _supplier = string.Empty;
    private int _quantity = 1;
    private double _unitWeightKg;
    private string _dimensions = string.Empty;
    private string _deliveryStatus = OrderProductInfo.DefaultDeliveryStatus;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(TotalWeightKg));
                OnPropertyChanged(nameof(TotalWeightKgRoundedUp));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string Supplier
    {
        get => _supplier;
        set
        {
            if (SetProperty(ref _supplier, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            var normalized = Math.Max(1, value);
            if (SetProperty(ref _quantity, normalized))
            {
                OnPropertyChanged(nameof(TotalWeightKg));
                OnPropertyChanged(nameof(TotalWeightKgRoundedUp));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public double UnitWeightKg
    {
        get => _unitWeightKg;
        set
        {
            var normalized = Math.Max(0d, value);
            if (SetProperty(ref _unitWeightKg, normalized))
            {
                OnPropertyChanged(nameof(TotalWeightKg));
                OnPropertyChanged(nameof(UnitWeightKgRoundedUp));
                OnPropertyChanged(nameof(TotalWeightKgRoundedUp));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string Dimensions
    {
        get => _dimensions;
        set
        {
            if (SetProperty(ref _dimensions, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string DeliveryStatus
    {
        get => _deliveryStatus;
        set
        {
            var normalized = OrderProductInfo.NormalizeDeliveryStatus(value);
            if (SetProperty(ref _deliveryStatus, normalized))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public double TotalWeightKg => Quantity * UnitWeightKg;

    public double UnitWeightKgRoundedUp => Math.Ceiling(UnitWeightKg * 100d) / 100d;

    public double TotalWeightKgRoundedUp => Math.Ceiling(TotalWeightKg * 100d) / 100d;

    public string Summary => OrderProductFormatter.BuildDetails([ToOrderProductInfo()]);

    public OrderProductInfo ToOrderProductInfo()
    {
        return new OrderProductInfo
        {
            Name = (Name ?? string.Empty).Trim(),
            Supplier = (Supplier ?? string.Empty).Trim(),
            Quantity = Math.Max(1, Quantity),
            UnitWeightKg = Math.Max(0d, UnitWeightKg),
            WeightKg = Math.Max(1, Quantity) * Math.Max(0d, UnitWeightKg),
            Dimensions = (Dimensions ?? string.Empty).Trim(),
            DeliveryStatus = OrderProductInfo.NormalizeDeliveryStatus(DeliveryStatus)
        };
    }

    public static ProductLineInput FromOrderProductInfo(OrderProductInfo product)
    {
        return new ProductLineInput
        {
            Name = product.Name ?? string.Empty,
            Supplier = product.Supplier ?? string.Empty,
            Quantity = Math.Max(1, product.Quantity),
            UnitWeightKg = OrderProductFormatter.ResolveUnitWeightKg(product),
            Dimensions = product.Dimensions ?? string.Empty,
            DeliveryStatus = OrderProductInfo.NormalizeDeliveryStatus(product.DeliveryStatus)
        };
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


