using System.Globalization;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class LoadPlanWindow : Window
{
    private Point _productDragStart;
    private LoadPlanProductItem? _productDragItem;
    private readonly Func<LoadPlanProductItem, Task<OrderProductInfo?>> _editProductAsync;
    private IReadOnlyList<AdditionalMaterialGroup> _availableAdditionalMaterialGroups;
    private readonly Func<Task<IReadOnlyList<AdditionalMaterialGroup>?>> _manageAdditionalMaterialGroupsAsync;
    private List<TourAdditionalMaterial> _selectedAdditionalMaterials;

    public LoadPlanWindow(string tourName, IReadOnlyList<LoadPlanOrderItem> orders,
        IReadOnlyList<LoadPlanProductItem> additionalMaterials,
        IReadOnlyList<LoadPlanAreaItem> loadingAreas, IReadOnlyList<TourLoadPlanPackage> savedPackages,
        IReadOnlyList<TourLoadPlanPlacement> savedPlacements,
        Func<LoadPlanProductItem, Task<OrderProductInfo?>> editProductAsync,
        IReadOnlyList<AdditionalMaterialGroup> availableAdditionalMaterialGroups,
        IReadOnlyList<TourAdditionalMaterial> selectedAdditionalMaterials,
        Func<Task<IReadOnlyList<AdditionalMaterialGroup>?>> manageAdditionalMaterialGroupsAsync)
    {
        InitializeComponent();
        _editProductAsync = editProductAsync;
        _availableAdditionalMaterialGroups = availableAdditionalMaterialGroups;
        _manageAdditionalMaterialGroupsAsync = manageAdditionalMaterialGroupsAsync;
        _selectedAdditionalMaterials = selectedAdditionalMaterials.Select(CloneAdditionalMaterial).ToList();
        TourName = tourName;
        Orders = orders;
        AdditionalMaterials = new ObservableCollection<LoadPlanProductItem>(additionalMaterials);
        ProductSummary = BuildProductSummary();
        DataContext = this;
        LoadPlanSurface.LoadingAreas = loadingAreas;
        LoadPlanSurface.ViewChanged += (_, _) => ZoomLevelText.Text = $"{LoadPlanSurface.ZoomPercent:0} %";
        LoadPackages(savedPackages);
        LoadPlanSurface.LoadPlacements(savedPlacements, AllPlacementItems());
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => LoadPlanSurface.ZoomOut();

    private void OnZoomInClick(object sender, RoutedEventArgs e) => LoadPlanSurface.ZoomIn();

    private void OnFitViewClick(object sender, RoutedEventArgs e) => LoadPlanSurface.FitView();

    private void OnSelectAdditionalMaterialClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AdditionalMaterialSelectionDialogWindow(
            _availableAdditionalMaterialGroups, _selectedAdditionalMaterials,
            ManageAdditionalMaterialGroupsAsync) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var updatedMaterials = dialog.Result.Select(CloneAdditionalMaterial).ToList();
        var updatedItems = updatedMaterials.Select(BuildAdditionalMaterialItem).Where(item => item is not null)
            .Cast<LoadPlanProductItem>().ToList();
        var updatedIds = updatedItems.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in AdditionalMaterials.Where(item => !updatedIds.Contains(item.Id)).ToList())
        {
            LoadPlanSurface.RemoveItem(removed);
            AdditionalMaterials.Remove(removed);
        }
        var existingIds = AdditionalMaterials.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var added in updatedItems.Where(item => !existingIds.Contains(item.Id))) AdditionalMaterials.Add(added);
        _selectedAdditionalMaterials = updatedMaterials;
        AdditionalMaterialsExpander.Visibility = AdditionalMaterialsVisibility;
        ProductSummaryText.Text = BuildProductSummary();
    }

    private async Task<IReadOnlyList<AdditionalMaterialGroup>?> ManageAdditionalMaterialGroupsAsync()
    {
        var groups = await _manageAdditionalMaterialGroupsAsync();
        if (groups is not null) _availableAdditionalMaterialGroups = groups;
        return groups;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        SavedPlacements = LoadPlanSurface.ExportPlacements();
        SavedPackages = Orders.SelectMany(order => order.Packages).Select(package => new TourLoadPlanPackage
        {
            Id = package.Id,
            OrderId = package.OrderId,
            Name = package.Name,
            PackageType = package.PackageType,
            WidthMm = package.PlacementItem.DimensionsMm.Width,
            DepthMm = package.PlacementItem.DimensionsMm.Depth,
            HeightMm = package.PlacementItem.DimensionsMm.Height,
            ProductIds = package.Products.Select(product => product.Id).ToList()
        }).ToList();
        SavedAdditionalMaterials = _selectedAdditionalMaterials.Select(CloneAdditionalMaterial).ToList();
        DialogResult = true;
    }

    private string BuildProductSummary()
    {
        var productCount = Orders.Sum(order => order.Products.Count);
        return AdditionalMaterials.Count == 0
            ? $"{Orders.Count} Aufträge · {productCount} Produkte"
            : $"{Orders.Count} Aufträge · {productCount} Produkte · {AdditionalMaterials.Count} Zusatzmaterial";
    }

    private static LoadPlanProductItem? BuildAdditionalMaterialItem(TourAdditionalMaterial group)
    {
        var width = group.WidthMm > 0 ? group.WidthMm : (group.Items ?? []).Select(item => Math.Max(0, item.WidthMm)).DefaultIfEmpty().Max();
        var depth = group.DepthMm > 0 ? group.DepthMm : (group.Items ?? []).Sum(item => Math.Max(0, item.DepthMm) * Math.Max(1, item.Quantity));
        var height = group.HeightMm > 0 ? group.HeightMm : (group.Items ?? []).Select(item => Math.Max(0, item.HeightMm)).DefaultIfEmpty().Max();
        if (!group.RequiresLoadingSpace || width <= 0 || depth <= 0) return null;
        var id = string.IsNullOrWhiteSpace(group.GroupId) ? Guid.NewGuid().ToString("N") : group.GroupId;
        return new LoadPlanProductItem($"material-group:{id}", group.GroupName, 1, 1,
            new ProductDimensionsMm(width, depth, height), Math.Max(0, group.WeightKg), "Z", "square");
    }

    private static TourAdditionalMaterial CloneAdditionalMaterial(TourAdditionalMaterial source) => new()
    {
        GroupId = source.GroupId, GroupName = source.GroupName, WeightKg = source.WeightKg,
        RequiresLoadingSpace = source.RequiresLoadingSpace, WidthMm = source.WidthMm,
        DepthMm = source.DepthMm, HeightMm = source.HeightMm,
        Items = (source.Items ?? []).Select(item => new AdditionalMaterialItem
        {
            Id = item.Id, Name = item.Name, Quantity = item.Quantity, WeightKg = item.WeightKg,
            WidthMm = item.WidthMm, DepthMm = item.DepthMm, HeightMm = item.HeightMm,
            RequiresLoadingSpace = item.RequiresLoadingSpace
        }).ToList()
    };

    private void OnCreatePackageClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LoadPlanOrderItem order) return;
        var products = order.Products.Where(product => product.IsSelectedForPackage && product.CanSelectForPackage).ToList();
        if (products.Count == 0)
        {
            MessageBox.Show(this, "Bitte zuerst mindestens ein verfügbares Produkt auswählen.", "Packstück",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new LoadPlanPackageDialogWindow($"Packstück {order.Packages.Count + 1}") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        AddPackage(order, $"package:{Guid.NewGuid():N}", dialog.PackageName, dialog.PackageType,
            dialog.WidthMm, dialog.DepthMm, dialog.HeightMm, products);
    }

    private void OnEditPackageClick(object sender, RoutedEventArgs e)
    {
        var package = (sender as MenuItem)?.CommandParameter as LoadPlanPackageItem
                      ?? (sender as FrameworkElement)?.DataContext as LoadPlanPackageItem;
        if (package is null) return;
        var dimensions = package.PlacementItem.DimensionsMm;
        var dialog = new LoadPlanPackageDialogWindow(package.Name, package.PackageType,
            dimensions.Width, dimensions.Depth, dimensions.Height) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var wasPlaced = package.PlacementItem.IsPlaced;
        LoadPlanSurface.RemoveItem(package.PlacementItem);
        package.Update(dialog.PackageName, dialog.PackageType,
            CreatePackagePlacementItem(package.Id, dialog.PackageName, dialog.WidthMm, dialog.DepthMm,
                dialog.HeightMm, package.Products));
        foreach (var product in package.Products) product.SetPackage(package.Id, package.Name);
        if (wasPlaced)
        {
            MessageBox.Show(this, "Die Maße wurden geändert. Bitte das Packstück erneut auf der Ladefläche platzieren.",
                "Packstück aktualisiert", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnDissolvePackageClick(object sender, RoutedEventArgs e)
    {
        var package = (sender as MenuItem)?.CommandParameter as LoadPlanPackageItem
                      ?? (sender as FrameworkElement)?.DataContext as LoadPlanPackageItem;
        if (package is null) return;
        LoadPlanSurface.RemoveItem(package.PlacementItem);
        foreach (var product in package.Products) product.SetPackage(string.Empty, string.Empty);
        Orders.First(order => string.Equals(order.LoadPlanKey, package.OrderId, StringComparison.OrdinalIgnoreCase))
            .Packages.Remove(package);
    }

    private void OnPackageMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: not null } button)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void OnTogglePackageClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LoadPlanPackageItem package)
            package.IsExpanded = !package.IsExpanded;
    }

    private void LoadPackages(IEnumerable<TourLoadPlanPackage>? packages)
    {
        foreach (var saved in packages ?? [])
        {
            var order = Orders.FirstOrDefault(item => string.Equals(item.LoadPlanKey, saved.OrderId, StringComparison.OrdinalIgnoreCase))
                        ?? Orders.FirstOrDefault(item => string.Equals(item.OrderId, saved.OrderId, StringComparison.OrdinalIgnoreCase));
            if (order is null) continue;
            var productIds = saved.ProductIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var products = order.Products.Where(product => productIds.Contains(product.Id) && !product.IsInPackage).ToList();
            if (products.Count == 0 || saved.WidthMm <= 0 || saved.DepthMm <= 0 || saved.HeightMm < 0) continue;
            AddPackage(order, saved.Id, saved.Name, saved.PackageType, saved.WidthMm, saved.DepthMm, saved.HeightMm, products);
        }
    }

    private void AddPackage(LoadPlanOrderItem order, string id, string name, string packageType,
        int widthMm, int depthMm, int heightMm, IReadOnlyList<LoadPlanProductItem> products)
    {
        var members = new ObservableCollection<LoadPlanProductItem>(products);
        var package = new LoadPlanPackageItem
        {
            Id = id,
            OrderId = order.LoadPlanKey,
            Name = name,
            PackageType = packageType,
            Products = members,
            PlacementItem = CreatePackagePlacementItem(id, name, widthMm, depthMm, heightMm, members)
        };
        foreach (var product in members) product.SetPackage(id, name);
        order.Packages.Add(package);
    }

    private static LoadPlanProductItem CreatePackagePlacementItem(string id, string name, int widthMm,
        int depthMm, int heightMm, IReadOnlyCollection<LoadPlanProductItem> products)
    {
        var first = products.First();
        return new LoadPlanProductItem(id, name, 1, 1, new ProductDimensionsMm(widthMm, depthMm, heightMm),
            products.Sum(product => product.WeightKg), first.StopLetter, first.MarkerShape)
        {
            PackageMembers = products.ToList()
        };
    }

    private IEnumerable<LoadPlanProductItem> AllPlacementItems() =>
        Orders.SelectMany(order => order.Products.Where(product => !product.IsInPackage)
            .Concat(order.Packages.Select(package => package.PlacementItem))).Concat(AdditionalMaterials);

    private void OnProductPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _productDragStart = e.GetPosition(this);
        _productDragItem = (sender as FrameworkElement)?.DataContext switch
        {
            LoadPlanProductItem product when !product.IsInPackage => product,
            LoadPlanPackageItem package => package.PlacementItem,
            _ => null
        };
    }

    private async void OnProductMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not LoadPlanProductItem product ||
            string.IsNullOrWhiteSpace(product.SourceOrderId) || string.IsNullOrWhiteSpace(product.SourceProductPositionId)) return;
        e.Handled = true;
        try
        {
            var updated = await _editProductAsync(product);
            if (updated is null) return;
            var matchingProducts = Orders.SelectMany(order => order.Products).Where(candidate =>
                         string.Equals(candidate.SourceOrderId, product.SourceOrderId, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(candidate.SourceProductPositionId, product.SourceProductPositionId, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var matching in matchingProducts)
            {
                var previousDimensions = matching.DimensionsMm;
                matching.UpdateFrom(updated);
                if (!matching.IsInPackage && matching.IsPlaced && previousDimensions != matching.DimensionsMm)
                    LoadPlanSurface.RemoveItem(matching);
            }
            foreach (var package in Orders.SelectMany(order => order.Packages)
                         .Where(package => package.Products.Any(member =>
                             string.Equals(member.SourceOrderId, product.SourceOrderId, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(member.SourceProductPositionId, product.SourceProductPositionId, StringComparison.OrdinalIgnoreCase))))
                package.RefreshSummary();
            LoadPlanSurface.InvalidateVisual();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Das Produkt konnte nicht gespeichert werden: {ex.Message}", "Produkt bearbeiten",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnProductMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _productDragItem is null || _productDragItem.IsPlaced)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _productDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _productDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _productDragItem;
        _productDragItem = null;
        if (sender is not UIElement dragSource) return;
        var previousOpacity = dragSource.Opacity;
        dragSource.Opacity = 0.55;
        dragSource.GiveFeedback += OnProductGiveFeedback;
        try
        {
            DragDrop.DoDragDrop(dragSource, new DataObject(typeof(LoadPlanProductItem), item), DragDropEffects.Move);
        }
        finally
        {
            dragSource.GiveFeedback -= OnProductGiveFeedback;
            dragSource.Opacity = previousOpacity;
            Mouse.SetCursor(null);
        }
    }

    private static void OnProductGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(Cursors.SizeAll);
        e.Handled = true;
    }

    public string TourName { get; }
    public string ProductSummary { get; }
    public IReadOnlyList<LoadPlanOrderItem> Orders { get; }
    public ObservableCollection<LoadPlanProductItem> AdditionalMaterials { get; }
    public Visibility AdditionalMaterialsVisibility => AdditionalMaterials.Count > 0
        ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<TourLoadPlanPlacement> SavedPlacements { get; private set; } = [];
    public IReadOnlyList<TourLoadPlanPackage> SavedPackages { get; private set; } = [];
    public IReadOnlyList<TourAdditionalMaterial> SavedAdditionalMaterials { get; private set; } = [];
}

public sealed class LoadPlanOrderItem : INotifyPropertyChanged
{
    public LoadPlanOrderItem(string loadPlanKey, string orderId, string customerName, string address,
        string stopLetter, string markerShape, IReadOnlyList<LoadPlanProductItem> products)
    {
        LoadPlanKey = loadPlanKey; OrderId = orderId; CustomerName = customerName; Address = address;
        StopLetter = stopLetter; MarkerShape = markerShape; Products = products;
        foreach (var product in products) product.PropertyChanged += OnProductPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string LoadPlanKey { get; }
    public string OrderId { get; }
    public string CustomerName { get; }
    public string Address { get; }
    public string StopLetter { get; }
    public string MarkerShape { get; }
    public IReadOnlyList<LoadPlanProductItem> Products { get; }
    public ObservableCollection<LoadPlanPackageItem> Packages { get; } = [];
    public string OrderHeading => $"{CustomerName} ({OrderId})";
    public int SelectedProductCount => Products.Count(product => product.IsSelectedForPackage && product.CanSelectForPackage);
    public bool HasSelectedProducts => SelectedProductCount > 0;
    public string PackageActionText => $"{SelectedProductCount} ausgewählt";

    private void OnProductPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(LoadPlanProductItem.IsSelectedForPackage) or
            nameof(LoadPlanProductItem.CanSelectForPackage) or nameof(LoadPlanProductItem.IsPlaced) or
            nameof(LoadPlanProductItem.IsInPackage))) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedProductCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedProducts)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PackageActionText)));
    }
}

public sealed class LoadPlanPackageItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _packageType = string.Empty;
    private LoadPlanProductItem _placementItem = null!;
    private bool _isExpanded;
    public event PropertyChangedEventHandler? PropertyChanged;
    public required string Id { get; init; }
    public required string OrderId { get; init; }
    public required string Name { get => _name; set => _name = value; }
    public required string PackageType { get => _packageType; set => _packageType = value; }
    public required ObservableCollection<LoadPlanProductItem> Products { get; init; }
    public required LoadPlanProductItem PlacementItem { get => _placementItem; set => _placementItem = value; }
    public string Heading => string.IsNullOrWhiteSpace(PackageType) ? Name : $"{PackageType}: {Name}";
    public string ProductSummary => $"{Products.Count} Produkte · {Products.Sum(x => x.WeightKg):0.##} kg";
    public string Dimensions => PlacementItem.CompactDimensions;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandGlyph)));
        }
    }
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    public void Update(string name, string packageType, LoadPlanProductItem placementItem)
    {
        Name = name; PackageType = packageType; PlacementItem = placementItem;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PackageType)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlacementItem)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Heading)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Dimensions)));
    }

    public void RefreshSummary() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProductSummary)));
}

public sealed class LoadPlanProductItem : INotifyPropertyChanged
{
    private bool _isPlaced;
    private bool _isSelectedForPackage;
    private string _packageId = string.Empty;
    private string _packageName = string.Empty;
    private string _name;
    private ProductDimensionsMm _dimensionsMm;
    private double _weightKg;

    public LoadPlanProductItem(string id, string name, int unitNumber, int totalQuantity,
        ProductDimensionsMm dimensions, double weightKg, string stopLetter, string markerShape,
        string sourceOrderId = "", string sourceProductPositionId = "")
    {
        Id = id;
        _name = name;
        UnitNumber = unitNumber;
        TotalQuantity = totalQuantity;
        _dimensionsMm = dimensions;
        _weightKg = weightKg;
        StopLetter = stopLetter;
        MarkerShape = markerShape;
        SourceOrderId = sourceOrderId;
        SourceProductPositionId = sourceProductPositionId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; }
    public string Name => _name;
    public int UnitNumber { get; }
    public int TotalQuantity { get; }
    public ProductDimensionsMm DimensionsMm => _dimensionsMm;
    public double WeightKg => _weightKg;
    public string StopLetter { get; }
    public string MarkerShape { get; }
    public IReadOnlyList<LoadPlanProductItem> PackageMembers { get; set; } = [];
    public string SourceOrderId { get; }
    public string SourceProductPositionId { get; }
    public bool HasDimensions => DimensionsMm.Width > 0 && DimensionsMm.Depth > 0;
    public string UnitText => TotalQuantity > 1 ? $"{UnitNumber}/{TotalQuantity}" : "1×";
    public string Dimensions => HasDimensions ? DimensionsMm.DisplayText : "Keine Abmessungen";
    public string CompactDimensions => !HasDimensions
        ? "Keine Abmessungen"
        : DimensionsMm.Height > 0
            ? $"{DimensionsMm.Width} × {DimensionsMm.Depth} × {DimensionsMm.Height} mm"
            : $"{DimensionsMm.Width} × {DimensionsMm.Depth} mm";
    public string WeightText => $"{WeightKg.ToString("0.##", CultureInfo.CurrentCulture)} kg";
    public bool IsInPackage => !string.IsNullOrWhiteSpace(PackageId);
    public bool CanSelectForPackage => !IsPlaced && !IsInPackage;
    public string PackageId => _packageId;
    public string PackageName => _packageName;

    public bool IsSelectedForPackage
    {
        get => _isSelectedForPackage;
        set
        {
            if (_isSelectedForPackage == value) return;
            _isSelectedForPackage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedForPackage)));
        }
    }

    public bool IsPlaced
    {
        get => _isPlaced;
        set
        {
            if (_isPlaced == value) return;
            _isPlaced = value;
            foreach (var member in PackageMembers) member.IsPlaced = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaced)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectForPackage)));
        }
    }

    public void SetPackage(string packageId, string packageName)
    {
        _packageId = packageId ?? string.Empty;
        _packageName = packageName ?? string.Empty;
        IsSelectedForPackage = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PackageId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PackageName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInPackage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectForPackage)));
    }

    public void UpdateFrom(OrderProductInfo product)
    {
        _name = product.Name?.Trim() ?? string.Empty;
        _weightKg = OrderProductFormatter.ResolveUnitWeightKg(product);
        var loadDimensions = string.IsNullOrWhiteSpace(product.EffectiveDimensions)
            ? product.Dimensions : product.EffectiveDimensions;
        _dimensionsMm = ProductDimensionsMm.TryParse(loadDimensions, out var parsed) ? parsed : default;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WeightKg)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WeightText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DimensionsMm)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Dimensions)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompactDimensions)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDimensions)));
    }
}

public sealed record LoadPlanAreaItem(
    string Id,
    int AssignmentNumber,
    bool IsTrailer,
    string Name,
    string LicensePlate,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    int MaxPayloadKg)
{
    public bool HasDimensions => LengthCm > 0 && WidthCm > 0;
    public string TypeLabel => IsTrailer ? "Anhänger" : "Fahrzeug";
    public string DimensionsText => HasDimensions
        ? $"{LengthCm / 100d:0.##} × {WidthCm / 100d:0.##} × {HeightCm / 100d:0.##} m"
        : "Innenmaße nicht hinterlegt";
}

public sealed class LoadPlanSurfaceControl : FrameworkElement
{
    private const double MinimumZoom = 0.5;
    private const double MaximumZoom = 3.0;
    private IReadOnlyList<LoadPlanAreaItem> _loadingAreas = [];
    private readonly List<PlacedLoadPlanProduct> _placedProducts = [];
    private readonly DispatcherTimer _previewTimer;
    private LoadPlanPlacementPreview? _placementPreview;
    private LoadPlanProductItem? _pendingProduct;
    private bool _pendingRotated;
    private PlacedLoadPlanProduct? _selectedProduct;
    private PlacedLoadPlanProduct? _movingProduct;
    private bool _movingRotated;
    private Vector _movePointerOffset;
    private Rect _rotateButtonBounds;
    private Rect _removeButtonBounds;
    private double _previewPhase;
    private double _zoom = 1;
    private Vector _panOffset;
    private bool _isPanning;
    private bool _spacePressed;
    private Point _lastPanPoint;

    public event EventHandler? ViewChanged;

    public double ZoomPercent => _zoom * 100;

    public LoadPlanSurfaceControl()
    {
        AllowDrop = true;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        MouseLeftButtonDown += OnSurfaceMouseLeftButtonDown;
        MouseMove += OnSurfaceMouseMove;
        MouseLeftButtonUp += OnSurfaceMouseLeftButtonUp;
        MouseDown += OnSurfaceMouseDown;
        MouseUp += OnSurfaceMouseUp;
        MouseWheel += OnSurfaceMouseWheel;
        KeyDown += OnSurfaceKeyDown;
        KeyUp += OnSurfaceKeyUp;
        LostMouseCapture += (_, _) => EndPan();
        Focusable = true;
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewPhase += 0.22;
            InvalidateVisual();
        };
    }

    public void ZoomIn() => SetZoom(_zoom * 1.2, new Point(ActualWidth / 2d, ActualHeight / 2d));

    public void ZoomOut() => SetZoom(_zoom / 1.2, new Point(ActualWidth / 2d, ActualHeight / 2d));

    public void FitView()
    {
        _zoom = 1;
        _panOffset = default;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<LoadPlanAreaItem> LoadingAreas
    {
        get => _loadingAreas;
        set
        {
            _loadingAreas = value ?? [];
            InvalidateVisual();
        }
    }

    public void LoadPlacements(IEnumerable<TourLoadPlanPlacement>? placements,
        IEnumerable<LoadPlanProductItem> products)
    {
        _placedProducts.Clear();
        var productsById = products.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var areasById = _loadingAreas.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var saved in placements ?? [])
        {
            if (!productsById.TryGetValue(saved.ProductId, out var product) || !product.HasDimensions ||
                !areasById.TryGetValue(saved.LoadingAreaId, out var area) || !area.HasDimensions) continue;
            var length = (saved.IsRotated ? product.DimensionsMm.Width : product.DimensionsMm.Depth) / 10d;
            var width = (saved.IsRotated ? product.DimensionsMm.Depth : product.DimensionsMm.Width) / 10d;
            var bounds = new Rect(saved.Xcm, saved.Ycm, length, width);
            if (bounds.Right > area.LengthCm || bounds.Bottom > area.WidthCm ||
                _placedProducts.Any(x => ReferenceEquals(x.Area, area) && x.AreaBounds.IntersectsWith(bounds))) continue;
            _placedProducts.Add(new PlacedLoadPlanProduct(product, area, bounds, saved.IsRotated));
            product.IsPlaced = true;
        }
        InvalidateVisual();
    }

    public IReadOnlyList<TourLoadPlanPlacement> ExportPlacements() => _placedProducts
        .Select(x => new TourLoadPlanPlacement
        {
            ProductId = x.Product.Id,
            LoadingAreaId = x.Area.Id,
            Xcm = Math.Round(x.AreaBounds.X, 2),
            Ycm = Math.Round(x.AreaBounds.Y, 2),
            IsRotated = x.IsRotated
        })
        .ToList();

    public void RemoveItem(LoadPlanProductItem item)
    {
        var placed = _placedProducts.FirstOrDefault(candidate => ReferenceEquals(candidate.Product, item));
        if (placed is null) return;
        item.IsPlaced = false;
        _placedProducts.Remove(placed);
        if (ReferenceEquals(_selectedProduct, placed)) _selectedProduct = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            new Pen(new SolidColorBrush(Color.FromRgb(215, 224, 236)), 1), bounds, 12, 12);

        if (_loadingAreas.Count == 0)
        {
            DrawCenteredText(drawingContext, "Kein Fahrzeug zugeordnet", 16, Brushes.SlateGray, bounds);
            return;
        }

        var validAreas = _loadingAreas.Where(area => area.HasDimensions).ToList();
        if (validAreas.Count == 0)
        {
            DrawCenteredText(drawingContext, "Für die zugeordneten Fahrzeuge sind keine Innenmaße hinterlegt.",
                15, Brushes.SlateGray, bounds);
            return;
        }

        var layouts = BuildAreaLayouts();
        var typeface = new Typeface("Segoe UI");

        foreach (var layout in layouts)
        {
            var area = layout.Area;
            var scale = layout.Scale;
            var rect = layout.Bounds;
            var width = rect.Width;
            var fill = area.IsTrailer
                ? new SolidColorBrush(Color.FromRgb(255, 247, 237))
                : new SolidColorBrush(Color.FromRgb(239, 246, 255));
            var stroke = area.IsTrailer
                ? new SolidColorBrush(Color.FromRgb(234, 88, 12))
                : new SolidColorBrush(Color.FromRgb(37, 99, 235));

            drawingContext.DrawRoundedRectangle(fill, new Pen(stroke, 2), rect, 8, 8);
            DrawGrid(drawingContext, rect, scale, stroke);

            if (!area.IsTrailer)
            {
                var cabWidth = Math.Min(34, Math.Max(16, width * 0.12));
                var cab = new Rect(rect.Right - cabWidth, rect.Top, cabWidth, rect.Height);
                drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(45, 37, 99, 235)), null, cab);
            }

            var title = new FormattedText($"{area.TypeLabel}: {area.Name}", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 13, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(title, new Point(rect.Left, rect.Top - 62));
            var dimensions = new FormattedText(area.DimensionsText, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 12, Brushes.SlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(dimensions, new Point(rect.Left, rect.Top - 43));
            var placedWeightKg = _placedProducts.Where(placed => ReferenceEquals(placed.Area, area))
                .Sum(placed => placed.Product.WeightKg);
            var weightText = area.MaxPayloadKg > 0
                ? $"{placedWeightKg.ToString("0.##", CultureInfo.CurrentCulture)} / {area.MaxPayloadKg.ToString("N0", CultureInfo.CurrentCulture)} kg"
                : $"{placedWeightKg.ToString("0.##", CultureInfo.CurrentCulture)} kg / Nutzlast fehlt";
            var weightBrush = area.MaxPayloadKg > 0 && placedWeightKg > area.MaxPayloadKg
                ? new SolidColorBrush(Color.FromRgb(220, 38, 38))
                : new SolidColorBrush(Color.FromRgb(22, 163, 74));
            var weight = new FormattedText(weightText, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 12, weightBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            weight.SetFontWeight(FontWeights.SemiBold);
            drawingContext.DrawText(weight, new Point(rect.Left, rect.Top - 24));
        }

        DrawPlacedProducts(drawingContext);
        DrawPlacementPreview(drawingContext);
        DrawSelectionActions(drawingContext);

        var missing = _loadingAreas.Where(area => !area.HasDimensions).ToList();
        if (missing.Count > 0)
        {
            var warning = string.Join(" · ", missing.Select(area => $"{area.TypeLabel} {area.Name}: Innenmaße fehlen"));
            var text = new FormattedText(warning, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, 12, new SolidColorBrush(Color.FromRgb(180, 83, 9)), VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, ActualWidth - 40)
            };
            drawingContext.DrawText(text, new Point(20, ActualHeight - text.Height - 16));
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(LoadPlanProductItem)) is not LoadPlanProductItem product ||
            product.IsPlaced || !product.HasDimensions)
        {
            ClearPlacementPreview();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        UpdatePlacementPreview(product, e.GetPosition(this), null, false);
        StartPreviewAnimation();
        InvalidateVisual();
        e.Effects = _placementPreview?.IsValid == true ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => ClearPlacementPreview();

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(LoadPlanProductItem)) is not LoadPlanProductItem product ||
            product.IsPlaced || !product.HasDimensions)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var preview = CreatePreview(product, e.GetPosition(this), null, false);
        if (preview is null || !preview.IsValid)
        {
            _pendingProduct = product;
            _pendingRotated = false;
            _placementPreview = preview;
            Focus();
            StartPreviewAnimation();
            InvalidateVisual();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var placed = new PlacedLoadPlanProduct(product, preview.Area, preview.AreaBounds, false);
        _placedProducts.Add(placed);
        _selectedProduct = placed;
        product.IsPlaced = true;
        _pendingProduct = null;
        ClearPlacementPreview();
        InvalidateVisual();
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private LoadPlanPlacementPreview? CreatePreview(LoadPlanProductItem product, Point pointer,
        PlacedLoadPlanProduct? ignoredProduct, bool rotated)
    {
        var target = BuildAreaLayouts().FirstOrDefault(layout => layout.Bounds.Contains(pointer));
        if (target is null) return null;

        var lengthCm = (rotated ? product.DimensionsMm.Width : product.DimensionsMm.Depth) / 10d;
        var widthCm = (rotated ? product.DimensionsMm.Depth : product.DimensionsMm.Width) / 10d;
        var offset = ignoredProduct is null ? new Vector(lengthCm / 2d, widthCm / 2d) : _movePointerOffset;
        var xCm = (pointer.X - target.Bounds.Left) / target.Scale - offset.X;
        var yCm = (pointer.Y - target.Bounds.Top) / target.Scale - offset.Y;
        var fits = lengthCm <= target.Area.LengthCm && widthCm <= target.Area.WidthCm;
        if (fits)
        {
            xCm = Math.Clamp(xCm, 0, target.Area.LengthCm - lengthCm);
            yCm = Math.Clamp(yCm, 0, target.Area.WidthCm - widthCm);
        }
        var candidate = new Rect(xCm, yCm, lengthCm, widthCm);
        var overlaps = _placedProducts.Any(placed => !ReferenceEquals(placed, ignoredProduct) &&
            ReferenceEquals(placed.Area, target.Area) && placed.AreaBounds.IntersectsWith(candidate));
        return new LoadPlanPlacementPreview(product, target.Area, candidate, fits && !overlaps, rotated);
    }

    private void UpdatePlacementPreview(LoadPlanProductItem product, Point pointer,
        PlacedLoadPlanProduct? ignoredProduct, bool rotated)
    {
        _placementPreview = CreatePreview(product, pointer, ignoredProduct, rotated);
    }

    private void OnSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this);
        Focus();
        if (_spacePressed)
        {
            BeginPan(point);
            e.Handled = true;
            return;
        }
        if (e.ClickCount == 2 && HitTestPlacedProduct(point) is null)
        {
            FitView();
            e.Handled = true;
            return;
        }
        if (_pendingProduct is not null)
        {
            if (_rotateButtonBounds.Contains(point))
            {
                RotatePendingProduct(point);
                e.Handled = true;
                return;
            }
            if (_removeButtonBounds.Contains(point))
            {
                CancelPendingPlacement();
                e.Handled = true;
                return;
            }
            var target = _placementPreview?.IsValid == true ? _placementPreview : null;
            if (target is not null)
            {
                var placed = new PlacedLoadPlanProduct(_pendingProduct, target.Area, target.AreaBounds, target.IsRotated);
                _placedProducts.Add(placed);
                _selectedProduct = placed;
                _pendingProduct.IsPlaced = true;
                _pendingProduct = null;
                ClearPlacementPreview();
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }
        if (_selectedProduct is not null && _rotateButtonBounds.Contains(point))
        {
            RotateSelectedProduct();
            e.Handled = true;
            return;
        }
        if (_selectedProduct is not null && _removeButtonBounds.Contains(point))
        {
            RemoveSelectedProduct();
            e.Handled = true;
            return;
        }

        var hit = HitTestPlacedProduct(point);
        _selectedProduct = hit;
        if (hit is null)
        {
            if (_pendingProduct is null)
            {
                BeginPan(point);
                e.Handled = true;
            }
            InvalidateVisual();
            return;
        }

        var layout = BuildAreaLayouts().First(candidate => ReferenceEquals(candidate.Area, hit.Area));
        _movePointerOffset = new Vector(
            (point.X - layout.Bounds.Left) / layout.Scale - hit.AreaBounds.X,
            (point.Y - layout.Bounds.Top) / layout.Scale - hit.AreaBounds.Y);
        _movingProduct = hit;
        _movingRotated = hit.IsRotated;
        _placementPreview = new LoadPlanPlacementPreview(hit.Product, hit.Area, hit.AreaBounds, true, hit.IsRotated);
        CaptureMouse();
        StartPreviewAnimation();
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var point = e.GetPosition(this);
            _panOffset += point - _lastPanPoint;
            _lastPanPoint = point;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_pendingProduct is not null && e.LeftButton != MouseButtonState.Pressed)
        {
            var pointer = e.GetPosition(this);
            if (_rotateButtonBounds.Contains(pointer) || _removeButtonBounds.Contains(pointer)) return;
            if (!BuildAreaLayouts().Any(layout => layout.Bounds.Contains(pointer))) return;
            UpdatePlacementPreview(_pendingProduct, pointer, null, _pendingRotated);
            StartPreviewAnimation();
            InvalidateVisual();
            return;
        }
        if (_movingProduct is null || e.LeftButton != MouseButtonState.Pressed)
        {
            if (_pendingProduct is null && !_spacePressed)
            {
                var pointer = e.GetPosition(this);
                var overAction = _rotateButtonBounds.Contains(pointer) || _removeButtonBounds.Contains(pointer);
                Cursor = overAction || HitTestPlacedProduct(pointer) is not null
                    ? Cursors.Arrow
                    : Cursors.Hand;
            }
            return;
        }
        UpdatePlacementPreview(_movingProduct.Product, e.GetPosition(this), _movingProduct, _movingRotated);
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnSurfaceMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            EndPan();
            e.Handled = true;
            return;
        }
        if (_movingProduct is null) return;
        if (_placementPreview?.IsValid == true)
        {
            _movingProduct.Area = _placementPreview.Area;
            _movingProduct.AreaBounds = _placementPreview.AreaBounds;
            _movingProduct.IsRotated = _placementPreview.IsRotated;
        }
        _movingProduct = null;
        ReleaseMouseCapture();
        ClearPlacementPreview();
        InvalidateVisual();
        e.Handled = true;
    }

    private PlacedLoadPlanProduct? HitTestPlacedProduct(Point point)
    {
        var layouts = BuildAreaLayouts();
        return _placedProducts.AsEnumerable().Reverse().FirstOrDefault(placed =>
        {
            var layout = layouts.FirstOrDefault(candidate => ReferenceEquals(candidate.Area, placed.Area));
            return layout is not null && ToScreenRect(placed.AreaBounds, layout).Contains(point);
        });
    }

    private void RotateSelectedProduct()
    {
        if (_selectedProduct is null) return;
        var selected = _selectedProduct;
        var newWidth = selected.AreaBounds.Height;
        var newHeight = selected.AreaBounds.Width;
        var center = new Point(selected.AreaBounds.X + selected.AreaBounds.Width / 2d,
            selected.AreaBounds.Y + selected.AreaBounds.Height / 2d);
        var x = Math.Clamp(center.X - newWidth / 2d, 0, Math.Max(0, selected.Area.LengthCm - newWidth));
        var y = Math.Clamp(center.Y - newHeight / 2d, 0, Math.Max(0, selected.Area.WidthCm - newHeight));
        var candidate = new Rect(x, y, newWidth, newHeight);
        var valid = newWidth <= selected.Area.LengthCm && newHeight <= selected.Area.WidthCm &&
            !_placedProducts.Any(placed => !ReferenceEquals(placed, selected) &&
                ReferenceEquals(placed.Area, selected.Area) && placed.AreaBounds.IntersectsWith(candidate));
        if (!valid) return;
        selected.AreaBounds = candidate;
        selected.IsRotated = !selected.IsRotated;
        InvalidateVisual();
    }

    private void RemoveSelectedProduct()
    {
        if (_selectedProduct is null) return;
        _selectedProduct.Product.IsPlaced = false;
        _placedProducts.Remove(_selectedProduct);
        _selectedProduct = null;
        InvalidateVisual();
    }

    private void RotatePendingProduct(Point pointer)
    {
        if (_pendingProduct is null) return;
        _pendingRotated = !_pendingRotated;
        var current = _placementPreview;
        var layout = current is null ? null : BuildAreaLayouts().FirstOrDefault(candidate => ReferenceEquals(candidate.Area, current.Area));
        var rotationPoint = layout is null
            ? pointer
            : new Point(layout.Bounds.Left + (current!.AreaBounds.X + current.AreaBounds.Width / 2d) * layout.Scale,
                layout.Bounds.Top + (current.AreaBounds.Y + current.AreaBounds.Height / 2d) * layout.Scale);
        UpdatePlacementPreview(_pendingProduct, rotationPoint, null, _pendingRotated);
        StartPreviewAnimation();
        InvalidateVisual();
    }

    private void CancelPendingPlacement()
    {
        _pendingProduct = null;
        _pendingRotated = false;
        ClearPlacementPreview();
        InvalidateVisual();
    }

    private void OnSurfaceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spacePressed = true;
            Cursor = Cursors.Hand;
            e.Handled = true;
            return;
        }
        if (_movingProduct is not null && e.Key == Key.R)
        {
            _movingRotated = !_movingRotated;
            UpdatePlacementPreview(_movingProduct.Product, Mouse.GetPosition(this), _movingProduct, _movingRotated);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_pendingProduct is null) return;
        if (e.Key == Key.R)
        {
            RotatePendingProduct(Mouse.GetPosition(this));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelPendingPlacement();
            e.Handled = true;
        }
    }

    private void OnSurfaceKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        _spacePressed = false;
        if (!_isPanning) Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        Focus();
        BeginPan(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnSurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !_isPanning) return;
        EndPan();
        e.Handled = true;
    }

    private void OnSurfaceMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetZoom(_zoom * (e.Delta > 0 ? 1.12 : 1d / 1.12), e.GetPosition(this));
        e.Handled = true;
    }

    private void BeginPan(Point point)
    {
        if (_movingProduct is not null || _pendingProduct is not null) return;
        _isPanning = true;
        _lastPanPoint = point;
        Cursor = Cursors.SizeAll;
        CaptureMouse();
    }

    private void EndPan()
    {
        if (!_isPanning) return;
        _isPanning = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = _spacePressed ? Cursors.Hand : Cursors.Arrow;
    }

    private void SetZoom(double requestedZoom, Point anchor)
    {
        var newZoom = Math.Clamp(requestedZoom, MinimumZoom, MaximumZoom);
        if (Math.Abs(newZoom - _zoom) < 0.0001) return;
        var ratio = newZoom / _zoom;
        var viewCenter = new Point(ActualWidth / 2d, ActualHeight / 2d + 15);
        var fromCenter = anchor - viewCenter;
        _panOffset = fromCenter - ratio * (fromCenter - _panOffset);
        _zoom = newZoom;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StartPreviewAnimation()
    {
        if (!_previewTimer.IsEnabled) _previewTimer.Start();
    }

    private void ClearPlacementPreview()
    {
        _placementPreview = null;
        _previewTimer.Stop();
        _previewPhase = 0;
        InvalidateVisual();
    }

    private IReadOnlyList<LoadPlanAreaLayout> BuildAreaLayouts()
    {
        var validAreas = _loadingAreas.Where(area => area.HasDimensions).ToList();
        if (validAreas.Count == 0) return [];
        const double gapCm = 120;
        var totalLengthCm = validAreas.Sum(area => area.LengthCm) + gapCm * Math.Max(0, validAreas.Count - 1);
        var maximumWidthCm = validAreas.Max(area => area.WidthCm);
        var fitScale = Math.Min(Math.Max(1, ActualWidth - 80) / totalLengthCm,
            Math.Max(1, ActualHeight - 175) / maximumWidthCm);
        var scale = fitScale * _zoom;
        var x = (ActualWidth - totalLengthCm * scale) / 2d + _panOffset.X;
        var centerY = ActualHeight / 2d + 15 + _panOffset.Y;
        var layouts = new List<LoadPlanAreaLayout>();
        foreach (var area in validAreas)
        {
            var bounds = new Rect(x, centerY - area.WidthCm * scale / 2d,
                area.LengthCm * scale, area.WidthCm * scale);
            layouts.Add(new LoadPlanAreaLayout(area, bounds, scale));
            x = bounds.Right + gapCm * scale;
        }
        return layouts;
    }

    private void DrawPlacedProducts(DrawingContext context)
    {
        if (_placedProducts.Count == 0) return;
        var layouts = BuildAreaLayouts();
        var typeface = new Typeface("Segoe UI");
        foreach (var placed in _placedProducts)
        {
            if (ReferenceEquals(placed, _movingProduct)) continue;
            var layout = layouts.FirstOrDefault(candidate => ReferenceEquals(candidate.Area, placed.Area));
            if (layout is null) continue;
            var rect = ToScreenRect(placed.AreaBounds, layout);
            var selected = ReferenceEquals(placed, _selectedProduct);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210, 34, 197, 94)),
                new Pen(selected ? Brushes.DarkViolet : new SolidColorBrush(Color.FromRgb(21, 128, 61)),
                    selected ? 3 : 1.5), rect, 4, 4);
            var markerSize = Math.Min(22, Math.Max(0, Math.Min(rect.Width - 6, rect.Height - 6)));
            if (markerSize >= 10)
            {
                LoadPlanStopMarkerControl.DrawMarker(context,
                    new Rect(rect.Left + 3, rect.Top + 3, markerSize, markerSize),
                    placed.Product.MarkerShape, placed.Product.StopLetter,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
            }
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var nameLeft = rect.Left + markerSize + 6;
            const double rightMeasureReserve = 20;
            var textWidth = Math.Max(1, rect.Right - rightMeasureReserve - nameLeft);
            var name = new FormattedText(placed.Product.Name, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 10, Brushes.White, pixelsPerDip)
            {
                MaxTextWidth = textWidth,
                MaxTextHeight = 14,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
            context.DrawText(name, new Point(nameLeft, rect.Top + 2));

            if (rect.Height >= 31)
            {
                var detailsText = placed.Product.CompactDimensions;
                var details = new FormattedText(detailsText, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface, 9, Brushes.White, pixelsPerDip)
                {
                    MaxTextWidth = textWidth,
                    MaxTextHeight = 13,
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis
                };
                context.DrawText(details, new Point(nameLeft, rect.Top + 16));

                if (rect.Width >= 54 && rect.Height >= 54)
                {
                    var zoomFontSize = Math.Clamp(11.9 * _zoom, 10.2, 25.5);
                    var availableFontSize = Math.Max(10, Math.Min(rect.Width / 4d, rect.Height / 3d));
                    var weightFontSize = Math.Min(zoomFontSize, availableFontSize);
                    var packageWeight = new FormattedText(placed.Product.WeightText,
                        CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                            FontWeights.Bold, FontStretches.Normal),
                        weightFontSize, new SolidColorBrush(Color.FromRgb(51, 65, 85)), pixelsPerDip)
                    {
                        MaxTextWidth = Math.Max(1, rect.Width - 24),
                        MaxLineCount = 1,
                        Trimming = TextTrimming.CharacterEllipsis,
                        TextAlignment = TextAlignment.Center
                    };
                    context.DrawText(packageWeight, new Point(rect.Left + 12,
                        rect.Top + (rect.Height - packageWeight.Height) / 2d));
                }
            }
            DrawProductEdgeDimensions(context, rect, placed.AreaBounds);
        }
    }

    private void DrawProductEdgeDimensions(DrawingContext context, Rect productRect, Rect areaBounds)
    {
        var depthText = $"{areaBounds.Width * 10:0} mm";
        var widthText = $"{areaBounds.Height * 10:0} mm";
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI Semibold");
        var background = new SolidColorBrush(Color.FromArgb(185, 21, 128, 61));

        var horizontal = new FormattedText(depthText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, 9, Brushes.White, pixelsPerDip);
        if (productRect.Width >= Math.Min(34, horizontal.Width + 6) && productRect.Height >= 22)
        {
            var labelWidth = Math.Min(productRect.Width - 4, horizontal.Width + 8);
            var labelBounds = new Rect(productRect.Left + (productRect.Width - labelWidth) / 2d,
                productRect.Bottom - 16, labelWidth, 14);
            context.DrawRoundedRectangle(background, null, labelBounds, 4, 4);
            context.PushClip(new RectangleGeometry(labelBounds));
            context.DrawText(horizontal, new Point(labelBounds.Left + (labelBounds.Width - horizontal.Width) / 2d,
                labelBounds.Top + (labelBounds.Height - horizontal.Height) / 2d));
            context.Pop();
        }

        var vertical = new FormattedText(widthText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, 9, Brushes.White, pixelsPerDip);
        if (productRect.Height >= Math.Min(34, vertical.Width + 6) && productRect.Width >= 22)
        {
            var labelWidth = Math.Min(productRect.Height - 4, vertical.Width + 8);
            var center = new Point(productRect.Right - 9, productRect.Top + productRect.Height / 2d);
            context.PushTransform(new RotateTransform(-90, center.X, center.Y));
            var labelBounds = new Rect(center.X - labelWidth / 2d, center.Y - 7, labelWidth, 14);
            context.DrawRoundedRectangle(background, null, labelBounds, 4, 4);
            context.PushClip(new RectangleGeometry(labelBounds));
            context.DrawText(vertical, new Point(labelBounds.Left + (labelBounds.Width - vertical.Width) / 2d,
                labelBounds.Top + (labelBounds.Height - vertical.Height) / 2d));
            context.Pop();
            context.Pop();
        }
    }

    private void DrawPlacementPreview(DrawingContext context)
    {
        if (_placementPreview is not null) DrawPreview(context, _placementPreview);
    }

    private void DrawPreview(DrawingContext context, LoadPlanPlacementPreview preview)
    {
        var layout = BuildAreaLayouts().FirstOrDefault(candidate => ReferenceEquals(candidate.Area, preview.Area));
        if (layout is null) return;
        var rect = ToScreenRect(preview.AreaBounds, layout);
        var valid = preview.IsValid;
        var alpha = (byte)(85 + 35 * (Math.Sin(_previewPhase) + 1d) / 2d);
        var color = valid ? Color.FromRgb(34, 197, 94) : Color.FromRgb(239, 68, 68);
        var fill = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        var pen = new Pen(new SolidColorBrush(color), 2) { DashStyle = DashStyles.Dash };
        context.DrawRoundedRectangle(fill, pen, rect, 5, 5);
        var dimensions = $"{preview.AreaBounds.Width * 10:0} × {preview.AreaBounds.Height * 10:0} mm";
        var text = new FormattedText(dimensions, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 11, valid ? Brushes.DarkGreen : Brushes.DarkRed,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var labelPoint = new Point(rect.Left, Math.Max(4, rect.Top - text.Height - 4));
        context.DrawText(text, labelPoint);
    }

    private void DrawSelectionActions(DrawingContext context)
    {
        _rotateButtonBounds = Rect.Empty;
        _removeButtonBounds = Rect.Empty;
        if (_pendingProduct is not null && _placementPreview is not null)
        {
            var preview = _placementPreview;
            var pendingLayout = BuildAreaLayouts().FirstOrDefault(candidate => ReferenceEquals(candidate.Area, preview.Area));
            if (pendingLayout is null) return;
            var previewRect = ToScreenRect(preview.AreaBounds, pendingLayout);
            const double pendingButtonWidth = 34;
            const double pendingButtonHeight = 34;
            var pendingTotalWidth = pendingButtonWidth * 2 + 6;
            var pendingLeft = Math.Clamp(previewRect.Left, 4, Math.Max(4, ActualWidth - pendingTotalWidth - 4));
            var pendingTop = previewRect.Top - pendingButtonHeight - 6;
            if (pendingTop < 4) pendingTop = previewRect.Bottom + 6;
            _rotateButtonBounds = new Rect(pendingLeft, pendingTop, pendingButtonWidth, pendingButtonHeight);
            _removeButtonBounds = new Rect(pendingLeft + pendingButtonWidth + 6, pendingTop, pendingButtonWidth, pendingButtonHeight);
            DrawActionButton(context, _rotateButtonBounds, true, Color.FromRgb(79, 70, 229));
            DrawActionButton(context, _removeButtonBounds, false, Color.FromRgb(100, 116, 139));
            return;
        }
        if (_selectedProduct is null || ReferenceEquals(_selectedProduct, _movingProduct)) return;
        var layout = BuildAreaLayouts().FirstOrDefault(candidate => ReferenceEquals(candidate.Area, _selectedProduct.Area));
        if (layout is null) return;
        var productRect = ToScreenRect(_selectedProduct.AreaBounds, layout);
        const double buttonWidth = 34;
        const double buttonHeight = 34;
        var totalWidth = buttonWidth * 2 + 6;
        var left = Math.Clamp(productRect.Left, 4, Math.Max(4, ActualWidth - totalWidth - 4));
        var top = productRect.Top - buttonHeight - 6;
        if (top < 4) top = productRect.Bottom + 6;
        _rotateButtonBounds = new Rect(left, top, buttonWidth, buttonHeight);
        _removeButtonBounds = new Rect(left + buttonWidth + 6, top, buttonWidth, buttonHeight);
        DrawActionButton(context, _rotateButtonBounds, true, Color.FromRgb(79, 70, 229));
        DrawActionButton(context, _removeButtonBounds, false, Color.FromRgb(220, 38, 38));
    }

    private static void DrawActionButton(DrawingContext context, Rect bounds, bool isRotate, Color color)
    {
        context.DrawRoundedRectangle(new SolidColorBrush(color), null, bounds, 5, 5);
        var center = new Point(bounds.Left + bounds.Width / 2d, bounds.Top + bounds.Height / 2d);
        var pen = new Pen(Brushes.White, 2.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        if (!isRotate)
        {
            const double offset = 5;
            context.DrawLine(pen, new Point(center.X - offset, center.Y - offset), new Point(center.X + offset, center.Y + offset));
            context.DrawLine(pen, new Point(center.X + offset, center.Y - offset), new Point(center.X - offset, center.Y + offset));
            return;
        }

        const double radius = 7;
        var start = new Point(center.X + radius * 0.5, center.Y + radius * 0.866);
        var end = new Point(center.X + radius * 0.866, center.Y - radius * 0.5);
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(start, false, false);
            figure.ArcTo(end, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);

        // Filled arrowhead aligned with the tangent of the clockwise arc.
        var arrow = new StreamGeometry();
        using (var figure = arrow.Open())
        {
            figure.BeginFigure(end, true, true);
            figure.LineTo(new Point(center.X + 1.4, center.Y - 6.6), true, false);
            figure.LineTo(new Point(center.X + 5.7, center.Y - 9.1), true, false);
        }
        arrow.Freeze();
        context.DrawGeometry(Brushes.White, null, arrow);
    }

    private static Rect ToScreenRect(Rect areaBounds, LoadPlanAreaLayout layout) => new(
        layout.Bounds.Left + areaBounds.X * layout.Scale,
        layout.Bounds.Top + areaBounds.Y * layout.Scale,
        areaBounds.Width * layout.Scale,
        areaBounds.Height * layout.Scale);

    private static void DrawGrid(DrawingContext context, Rect rect, double scale, Brush stroke)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(45, 71, 85, 105)), 1);
        var step = 100d * scale;
        if (step < 14) return;
        for (var x = rect.Left + step; x < rect.Right; x += step)
            context.DrawLine(pen, new Point(x, rect.Top), new Point(x, rect.Bottom));
        for (var y = rect.Top + step; y < rect.Bottom; y += step)
            context.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
    }

    private void DrawCenteredText(DrawingContext context, string value, double size, Brush brush, Rect bounds)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, bounds.Width - 60),
            TextAlignment = TextAlignment.Center
        };
        context.DrawText(text, new Point(30, (bounds.Height - text.Height) / 2d));
    }
}

public sealed class LoadPlanStopMarkerControl : FrameworkElement
{
    public static readonly DependencyProperty LetterProperty = DependencyProperty.Register(
        nameof(Letter), typeof(string), typeof(LoadPlanStopMarkerControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(
        nameof(Shape), typeof(string), typeof(LoadPlanStopMarkerControl),
        new FrameworkPropertyMetadata("circle", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(LoadPlanStopMarkerControl),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public string Letter
    {
        get => (string)GetValue(LetterProperty);
        set => SetValue(LetterProperty, value);
    }

    public string Shape
    {
        get => (string)GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        DrawMarker(drawingContext, new Rect(0, 0, ActualWidth, ActualHeight), Shape, Letter,
            VisualTreeHelper.GetDpi(this).PixelsPerDip, Fill);
    }

    internal static void DrawMarker(DrawingContext context, Rect bounds, string? shape, string? letter,
        double pixelsPerDip, Brush? fillBrush = null)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var fill = fillBrush ?? new SolidColorBrush(Color.FromRgb(22, 163, 74));
        var stroke = new Pen(Brushes.White, Math.Max(1, Math.Min(bounds.Width, bounds.Height) * 0.06));
        var normalized = (shape ?? "circle").Trim().ToLowerInvariant();
        if (normalized == "triangle")
        {
            var geometry = new StreamGeometry();
            using (var writer = geometry.Open())
            {
                writer.BeginFigure(new Point(bounds.Left + bounds.Width / 2d, bounds.Top), true, true);
                writer.LineTo(new Point(bounds.Right, bounds.Bottom), true, false);
                writer.LineTo(new Point(bounds.Left, bounds.Bottom), true, false);
            }
            geometry.Freeze();
            context.DrawGeometry(fill, stroke, geometry);
        }
        else if (normalized == "square")
        {
            context.DrawRoundedRectangle(fill, stroke, bounds, bounds.Width * 0.12, bounds.Height * 0.12);
        }
        else
        {
            context.DrawEllipse(fill, stroke, new Point(bounds.Left + bounds.Width / 2d, bounds.Top + bounds.Height / 2d),
                bounds.Width / 2d, bounds.Height / 2d);
        }

        var text = new FormattedText(letter ?? string.Empty, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"),
            Math.Max(7, Math.Min(bounds.Width, bounds.Height) * 0.48), Brushes.White, pixelsPerDip);
        var glyph = text.BuildGeometry(new Point(0, 0));
        var glyphBounds = glyph.Bounds;
        var centerX = bounds.Left + bounds.Width / 2d;
        var centerY = bounds.Top + bounds.Height * (normalized == "triangle" ? 0.60 : 0.50);
        context.PushTransform(new TranslateTransform(
            centerX - glyphBounds.Left - glyphBounds.Width / 2d,
            centerY - glyphBounds.Top - glyphBounds.Height / 2d));
        context.DrawGeometry(Brushes.White, null, glyph);
        context.Pop();
    }
}

internal sealed record LoadPlanAreaLayout(LoadPlanAreaItem Area, Rect Bounds, double Scale);
internal sealed record LoadPlanPlacementPreview(
    LoadPlanProductItem Product,
    LoadPlanAreaItem Area,
    Rect AreaBounds,
    bool IsValid,
    bool IsRotated);

internal sealed class PlacedLoadPlanProduct(
    LoadPlanProductItem product,
    LoadPlanAreaItem area,
    Rect areaBounds,
    bool isRotated)
{
    public LoadPlanProductItem Product { get; } = product;
    public LoadPlanAreaItem Area { get; set; } = area;
    public Rect AreaBounds { get; set; } = areaBounds;
    public bool IsRotated { get; set; } = isRotated;
}
