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
    private void OnAddItem(object sender, RoutedEventArgs e) => ViewModel.SelectedGroup?.Items.Add(new MaterialItemEditor { Name = "Neues Werkzeug" });
    private void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGroup is not null && ItemsGrid.SelectedItem is MaterialItemEditor selected)
        {
            ViewModel.SelectedGroup.Items.Remove(selected);
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
        }
        result = Groups.Select(x => x.ToModel()).ToList(); return true;
    }
}

public sealed class MaterialGroupEditor : ObservableObject
{
    private string _name = string.Empty; private string _totalWeightText = string.Empty;
    public MaterialGroupEditor() { InitializeItemTracking(); }
    public MaterialGroupEditor(AdditionalMaterialGroup source) { Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id; _name = source.Name; _totalWeightText = source.TotalWeightKg?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty; Items = new((source.Items ?? []).Select(x => new MaterialItemEditor(x))); InitializeItemTracking(); }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string TotalWeightText { get => _totalWeightText; set { if (SetProperty(ref _totalWeightText, value)) OnPropertyChanged(nameof(DisplayWeightText)); } }
    public ObservableCollection<MaterialItemEditor> Items { get; set; } = new();
    public string DisplayWeightText
    {
        get
        {
            if (TryGetTotalWeight(out var total, out _) && total.HasValue)
            {
                return $"{total.Value:0.##} kg";
            }
            var sum = Items.Where(x => x.TryGetWeight(out _)).Sum(x => { x.TryGetWeight(out var weight); return weight; });
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
            OnPropertyChanged(nameof(DisplayWeightText));
        };
    }
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaterialItemEditor.WeightText)) OnPropertyChanged(nameof(DisplayWeightText));
    }
    public bool TryGetTotalWeight(out double? value, out string error) { value = null; error = string.Empty; if (string.IsNullOrWhiteSpace(TotalWeightText)) return true; if (!double.TryParse(TotalWeightText, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) || parsed < 0) { error = $"Das Gesamtgewicht der Gruppe „{Name}“ ist ungültig."; return false; } value = parsed; return true; }
    public AdditionalMaterialGroup ToModel() { TryGetTotalWeight(out var total, out _); return new() { Id = Id, Name = Name.Trim(), TotalWeightKg = total, Items = Items.Select(x => x.ToModel()).ToList() }; }
}

public sealed class MaterialItemEditor : ObservableObject
{
    private string _name = string.Empty; private string _weightText = "0";
    public MaterialItemEditor() { }
    public MaterialItemEditor(AdditionalMaterialItem source) { _name = source.Name; _weightText = source.WeightKg.ToString("0.##", CultureInfo.CurrentCulture); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string WeightText { get => _weightText; set => SetProperty(ref _weightText, value); }
    public bool TryGetWeight(out double value) => double.TryParse(WeightText, NumberStyles.Number, CultureInfo.CurrentCulture, out value) && value >= 0;
    public AdditionalMaterialItem ToModel() { TryGetWeight(out var weight); return new() { Name = Name.Trim(), WeightKg = weight }; }
}
