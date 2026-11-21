using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class AdditionalMaterialGroupsDialogWindow : Window
{
    public AdditionalMaterialGroupsDialogWindow(IEnumerable<AdditionalMaterialGroup>? groups)
    {
        InitializeComponent();
        ViewModel = new AdditionalMaterialGroupsEditorViewModel(groups);
        DataContext = ViewModel;
    }

    public AdditionalMaterialGroupsEditorViewModel ViewModel { get; }
    public IReadOnlyList<AdditionalMaterialGroup> Result { get; private set; } = [];

    private void OnAddGroup(object sender, RoutedEventArgs e) => ViewModel.AddGroup();
    private void OnRemoveGroup(object sender, RoutedEventArgs e) => ViewModel.RemoveSelectedGroup();
    private void OnAddItem(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGroup is not { } group) return;
        var item = new MaterialItemEditor { Name = "Neue Position" };
        var dialog = new AdditionalMaterialItemDialogWindow(item) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        CopyItem(dialog.Item, item);
        group.Items.Add(item);
        group.SelectedItem = item;
        ItemsGrid.ScrollIntoView(item);
    }
    private void OnEditItem(object sender, RoutedEventArgs e) => EditSelectedItem();
    private void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => EditSelectedItem();

    private void EditSelectedItem()
    {
        if (ViewModel.SelectedGroup?.SelectedItem is not { } item) return;
        var dialog = new AdditionalMaterialItemDialogWindow(item) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        CopyItem(dialog.Item, item);
    }

    private static void CopyItem(MaterialItemEditor source, MaterialItemEditor target)
    {
        target.Name = source.Name.Trim();
        target.QuantityText = source.QuantityText;
        target.WeightText = source.WeightText;
        target.RequiresLoadingSpace = source.RequiresLoadingSpace;
        target.WidthText = source.WidthText;
        target.DepthText = source.DepthText;
        target.HeightText = source.HeightText;
    }
    private void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGroup is not null && ItemsGrid.SelectedItem is MaterialItemEditor selected)
        {
            ViewModel.SelectedGroup.Items.Remove(selected);
            ViewModel.SelectedGroup.SelectedItem = ViewModel.SelectedGroup.Items.FirstOrDefault();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuild(out var result, out var error))
        {
            Services.AppMessageBox.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = result;
        DialogResult = true;
    }
}

public sealed class AdditionalMaterialGroupsEditorViewModel : ObservableObject
{
    private MaterialGroupEditor? _selectedGroup;
    public AdditionalMaterialGroupsEditorViewModel(IEnumerable<AdditionalMaterialGroup>? groups)
    {
        Groups = new ObservableCollection<MaterialGroupEditor>((groups ?? []).Select(x => new MaterialGroupEditor(x)));
        SelectedGroup = Groups.FirstOrDefault();
    }
    public ObservableCollection<MaterialGroupEditor> Groups { get; }
    public MaterialGroupEditor? SelectedGroup { get => _selectedGroup; set => SetProperty(ref _selectedGroup, value); }
    public void AddGroup() { var group = new MaterialGroupEditor { Name = "Neue Werkzeuggruppe" }; Groups.Add(group); SelectedGroup = group; }
    public void RemoveSelectedGroup() { if (SelectedGroup is null) return; var i = Groups.IndexOf(SelectedGroup); Groups.Remove(SelectedGroup); SelectedGroup = Groups.ElementAtOrDefault(Math.Min(i, Groups.Count - 1)); }
    public bool TryBuild(out IReadOnlyList<AdditionalMaterialGroup> result, out string error)
    {
        result = []; error = string.Empty;
        if (Groups.Any(x => string.IsNullOrWhiteSpace(x.Name))) { error = "Jede Werkzeuggruppe benötigt einen Namen."; return false; }
        if (Groups.GroupBy(x => x.Name.Trim(), StringComparer.CurrentCultureIgnoreCase).Any(x => x.Count() > 1)) { error = "Die Namen der Werkzeuggruppen müssen eindeutig sein."; return false; }
        foreach (var group in Groups)
        {
            if (!group.TryGetTotalWeight(out _, out error)) return false;
            if (group.Items.Any(x => string.IsNullOrWhiteSpace(x.Name))) { error = $"In der Gruppe „{group.Name}“ benötigt jede Position einen Namen."; return false; }
            if (group.Items.Any(x => !x.TryGetWeight(out _))) { error = $"In der Gruppe „{group.Name}“ enthält mindestens eine Position ein ungültiges Gewicht."; return false; }
            if (group.Items.Any(x => !x.TryGetQuantity(out _))) { error = $"In der Gruppe „{group.Name}“ enthält mindestens eine Position eine ungültige Menge."; return false; }
            if (!group.TryGetDimensions(out _, out _, out _)) { error = $"Für die platzrelevante Gruppe „{group.Name}“ sind Breite und Tiefe als positive Millimeterwerte erforderlich."; return false; }
        }
        result = Groups.Select(x => x.ToModel()).ToList(); return true;
    }
}

public sealed class MaterialGroupEditor : ObservableObject
{
    private string _name = string.Empty; private string _totalWeightText = string.Empty; private MaterialItemEditor? _selectedItem; private bool _requiresLoadingSpace;
    private string _widthText = string.Empty; private string _depthText = string.Empty; private string _heightText = string.Empty;
    public MaterialGroupEditor() { InitializeItemTracking(); }
    public MaterialGroupEditor(AdditionalMaterialGroup source)
    {
        Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id;
        _name = source.Name;
        _totalWeightText = source.TotalWeightKg?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
        Items = new((source.Items ?? []).Select(x => new MaterialItemEditor(x)));
        _requiresLoadingSpace = source.RequiresLoadingSpace || Items.Any(item => item.RequiresLoadingSpace);
        var legacyItems = source.Items ?? [];
        var width = source.WidthMm > 0 ? source.WidthMm : legacyItems.Select(x => Math.Max(0, x.WidthMm)).DefaultIfEmpty().Max();
        var depth = source.DepthMm > 0 ? source.DepthMm : legacyItems.Sum(x => Math.Max(0, x.DepthMm) * Math.Max(1, x.Quantity));
        var height = source.HeightMm > 0 ? source.HeightMm : legacyItems.Select(x => Math.Max(0, x.HeightMm)).DefaultIfEmpty().Max();
        _widthText = width > 0 ? width.ToString(CultureInfo.CurrentCulture) : string.Empty;
        _depthText = depth > 0 ? depth.ToString(CultureInfo.CurrentCulture) : string.Empty;
        _heightText = height > 0 ? height.ToString(CultureInfo.CurrentCulture) : string.Empty;
        InitializeItemTracking();
        ApplyLoadingSpaceToItems();
    }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public bool RequiresLoadingSpace
    {
        get => _requiresLoadingSpace;
        set { if (SetProperty(ref _requiresLoadingSpace, value)) ApplyLoadingSpaceToItems(); }
    }
    public string WidthText { get => _widthText; set => SetProperty(ref _widthText, value); }
    public string DepthText { get => _depthText; set => SetProperty(ref _depthText, value); }
    public string HeightText { get => _heightText; set => SetProperty(ref _heightText, value); }
    public string TotalWeightText { get => _totalWeightText; set { if (SetProperty(ref _totalWeightText, value)) { OnPropertyChanged(nameof(DisplayWeightText)); OnPropertyChanged(nameof(UseManualTotalWeight)); } } }
    public bool UseManualTotalWeight
    {
        get => !string.IsNullOrWhiteSpace(TotalWeightText);
        set
        {
            if (value == UseManualTotalWeight) return;
            TotalWeightText = value ? DisplayWeightText.Replace(" kg", string.Empty) : string.Empty;
            OnPropertyChanged();
        }
    }
    public ObservableCollection<MaterialItemEditor> Items { get; set; } = new();
    public MaterialItemEditor? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }
    public string DisplayWeightText
    {
        get
        {
            if (TryGetTotalWeight(out var total, out _) && total.HasValue)
            {
                return $"{total.Value:0.##} kg";
            }
            var sum = Items.Where(x => x.TryGetWeight(out _) && x.TryGetQuantity(out _)).Sum(x => { x.TryGetWeight(out var weight); x.TryGetQuantity(out var quantity); return weight * quantity; });
            return $"{sum:0.##} kg";
        }
    }
    private void InitializeItemTracking()
    {
        foreach (var item in Items) item.PropertyChanged += OnItemPropertyChanged;
        Items.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null) foreach (MaterialItemEditor item in e.OldItems) item.PropertyChanged -= OnItemPropertyChanged;
            if (e.NewItems is not null) foreach (MaterialItemEditor item in e.NewItems) item.PropertyChanged += OnItemPropertyChanged;
            if (e.NewItems is not null) foreach (MaterialItemEditor item in e.NewItems) item.RequiresLoadingSpace = RequiresLoadingSpace;
            OnPropertyChanged(nameof(DisplayWeightText));
            OnPropertyChanged(nameof(PositionSummary));
            SelectedItem ??= Items.FirstOrDefault();
        };
        SelectedItem = Items.FirstOrDefault();
    }
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaterialItemEditor.WeightText) or nameof(MaterialItemEditor.QuantityText))
            OnPropertyChanged(nameof(DisplayWeightText));
    }
    public bool TryGetTotalWeight(out double? value, out string error) { value = null; error = string.Empty; if (string.IsNullOrWhiteSpace(TotalWeightText)) return true; if (!double.TryParse(TotalWeightText, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) || parsed < 0) { error = $"Das Gesamtgewicht der Gruppe „{Name}“ ist ungültig."; return false; } value = parsed; return true; }
    private void ApplyLoadingSpaceToItems()
    {
        foreach (var item in Items) item.RequiresLoadingSpace = RequiresLoadingSpace;
    }
    public bool TryGetDimensions(out int width, out int depth, out int height)
    {
        width = depth = height = 0;
        if (!RequiresLoadingSpace) return true;
        var validHeight = string.IsNullOrWhiteSpace(HeightText) || int.TryParse(HeightText, out height) && height >= 0;
        return int.TryParse(WidthText, out width) && width > 0 && int.TryParse(DepthText, out depth) && depth > 0 && validHeight;
    }
    public AdditionalMaterialGroup ToModel() { TryGetTotalWeight(out var total, out _); TryGetDimensions(out var width, out var depth, out var height); return new() { Id = Id, Name = Name.Trim(), TotalWeightKg = total, RequiresLoadingSpace = RequiresLoadingSpace, WidthMm = width, DepthMm = depth, HeightMm = height, Items = Items.Select(x => x.ToModel()).ToList() }; }
    public string PositionSummary => $"{Items.Count} Position(en)";
}

public sealed class MaterialItemEditor : ObservableObject
{
    private string _name = string.Empty; private string _weightText = "0"; private string _quantityText = "1";
    private string _widthText = string.Empty; private string _depthText = string.Empty; private string _heightText = string.Empty;
    private bool _requiresLoadingSpace;
    public MaterialItemEditor() { }
    public MaterialItemEditor(AdditionalMaterialItem source) { Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id; _name = source.Name; _quantityText = Math.Max(1, source.Quantity).ToString(CultureInfo.CurrentCulture); _weightText = source.WeightKg.ToString("0.##", CultureInfo.CurrentCulture); _widthText = source.WidthMm > 0 ? source.WidthMm.ToString(CultureInfo.CurrentCulture) : string.Empty; _depthText = source.DepthMm > 0 ? source.DepthMm.ToString(CultureInfo.CurrentCulture) : string.Empty; _heightText = source.HeightMm > 0 ? source.HeightMm.ToString(CultureInfo.CurrentCulture) : string.Empty; _requiresLoadingSpace = source.RequiresLoadingSpace; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string QuantityText { get => _quantityText; set { if (SetProperty(ref _quantityText, value)) { OnPropertyChanged(nameof(QuantitySummary)); OnPropertyChanged(nameof(WeightSummary)); } } }
    public string WeightText { get => _weightText; set { if (SetProperty(ref _weightText, value)) OnPropertyChanged(nameof(WeightSummary)); } }
    public bool RequiresLoadingSpace { get => _requiresLoadingSpace; set { if (SetProperty(ref _requiresLoadingSpace, value)) OnPropertyChanged(nameof(LoadingSpaceSummary)); } }
    public string WidthText { get => _widthText; set { if (SetProperty(ref _widthText, value)) OnPropertyChanged(nameof(LoadingSpaceSummary)); } }
    public string DepthText { get => _depthText; set { if (SetProperty(ref _depthText, value)) OnPropertyChanged(nameof(LoadingSpaceSummary)); } }
    public string HeightText { get => _heightText; set { if (SetProperty(ref _heightText, value)) OnPropertyChanged(nameof(LoadingSpaceSummary)); } }
    public string QuantitySummary => TryGetQuantity(out var quantity) ? $"{quantity} Stück" : "Menge ungültig";
    public string WeightSummary
    {
        get
        {
            if (!TryGetWeight(out var weight)) return "Gewicht ungültig";
            TryGetQuantity(out var quantity);
            return $"{weight:0.##} kg/Stk. · {weight * Math.Max(1, quantity):0.##} kg";
        }
    }
    public string LoadingSpaceSummary
    {
        get
        {
            if (!RequiresLoadingSpace) return "Kein Ladeflächenbedarf";
            if (!TryGetDimensions(out var width, out var depth, out var height)) return "Maße fehlen";
            return height > 0 ? $"{width} × {depth} × {height} mm" : $"{width} × {depth} mm";
        }
    }
    public bool TryGetWeight(out double value) => double.TryParse(WeightText, NumberStyles.Number, CultureInfo.CurrentCulture, out value) && value >= 0;
    public bool TryGetQuantity(out int value) => int.TryParse(QuantityText, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && value > 0;
    public bool TryGetDimensions(out int width, out int depth, out int height)
    {
        width = depth = height = 0;
        if (!RequiresLoadingSpace) return true;
        var validHeight = string.IsNullOrWhiteSpace(HeightText) || int.TryParse(HeightText, out height) && height >= 0;
        return int.TryParse(WidthText, out width) && width > 0 && int.TryParse(DepthText, out depth) && depth > 0 && validHeight;
    }
    public AdditionalMaterialItem ToModel() { TryGetWeight(out var weight); TryGetQuantity(out var quantity); TryGetDimensions(out var width, out var depth, out var height); return new() { Id = Id, Name = Name.Trim(), Quantity = quantity, WeightKg = weight, RequiresLoadingSpace = RequiresLoadingSpace, WidthMm = width, DepthMm = depth, HeightMm = height }; }
}
