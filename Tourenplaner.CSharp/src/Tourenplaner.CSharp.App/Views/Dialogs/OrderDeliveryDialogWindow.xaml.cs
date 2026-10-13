using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class OrderDeliveryDialogWindow : Window
{
    public OrderDeliveryDialogWindow(Order order)
    {
        InitializeComponent();
        ViewModel = new OrderDeliveryDialogViewModel(order);
        DataContext = ViewModel;
    }

    public OrderDeliveryDialogViewModel ViewModel { get; }
    public IReadOnlyList<OrderDeliveryLine>? ResultLines { get; private set; }
    public DateOnly? Delivery1PlannedDate { get; private set; }
    public DateOnly? Delivery2PlannedDate { get; private set; }
    public string Delivery1Notes { get; private set; } = string.Empty;
    public string Delivery2Notes { get; private set; } = string.Empty;

    private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuild(out var lines, out var error))
        {
            AppMessageBox.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ResultLines = lines;
        Delivery1PlannedDate = ViewModel.Delivery1PlannedDate.HasValue ? DateOnly.FromDateTime(ViewModel.Delivery1PlannedDate.Value) : null;
        Delivery2PlannedDate = ViewModel.Delivery2PlannedDate.HasValue ? DateOnly.FromDateTime(ViewModel.Delivery2PlannedDate.Value) : null;
        Delivery1Notes = ViewModel.Delivery1Notes;
        Delivery2Notes = ViewModel.Delivery2Notes;
        DialogResult = true;
        Close();
    }
}

public sealed class OrderDeliveryDialogViewModel : INotifyPropertyChanged
{
    public OrderDeliveryDialogViewModel(Order order)
    {
        var service = new OrderDeliveryService();
        var remaining = service.GetRemainingQuantities(order);
        Lines = new ObservableCollection<OrderDeliveryLineInput>(order.Products
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && remaining.GetValueOrDefault(x.PositionId) > 0)
            .Select(x => new OrderDeliveryLineInput(x.PositionId, x.Name, OrderProductInfo.NormalizeQuantity(x.Quantity), remaining[x.PositionId])));
        foreach (var line in Lines) line.PropertyChanged += (_, _) => PropertyChanged?.Invoke(this, new(nameof(Summary)));
        Delivery1PlannedDate = order.DeliveryDate?.ToDateTime(TimeOnly.MinValue);
        Delivery2PlannedDate = order.DeliveryDate?.ToDateTime(TimeOnly.MinValue);
        Delivery1Notes = order.Notes ?? string.Empty;
        Delivery2Notes = order.Notes ?? string.Empty;
    }
    public ObservableCollection<OrderDeliveryLineInput> Lines { get; }
    public DateTime? Delivery1PlannedDate { get; set; }
    public DateTime? Delivery2PlannedDate { get; set; }
    public string Delivery1Notes { get; set; } = string.Empty;
    public string Delivery2Notes { get; set; } = string.Empty;
    public string Summary => $"Ausgewählte Menge: {Lines.Sum(x => Math.Max(0, x.Quantity))}";
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool TryBuild(out IReadOnlyList<OrderDeliveryLine> lines, out string error)
    {
        error = string.Empty;
        if (Lines.Any(x => x.IsSelected && (!x.HasValidQuantity || x.Quantity < 1 || x.Quantity > x.RemainingQuantity)))
        {
            lines = [];
            error = "Eine Liefermenge liegt ausserhalb der verfügbaren Restmenge.";
            return false;
        }
        lines = Lines.Where(x => x.IsSelected && x.Quantity > 0).Select(x => new OrderDeliveryLine { OrderProductPositionId = x.PositionId, Quantity = x.Quantity }).ToList();
        if (lines.Count == 0)
        {
            error = "Bitte mindestens eine Menge für diese Lieferung eingeben.";
            return false;
        }
        return true;
    }
}

public sealed class OrderDeliveryLineInput : INotifyPropertyChanged
{
    private int _quantity;
    private bool _isSelected;
    private string _quantityText = "0";
    public OrderDeliveryLineInput(string positionId, string name, int orderedQuantity, int remainingQuantity)
    { PositionId = positionId; Name = name; OrderedQuantity = orderedQuantity; RemainingQuantity = remainingQuantity; }
    public string PositionId { get; }
    public string Name { get; }
    public int OrderedQuantity { get; }
    public int RemainingQuantity { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            if (value && Quantity <= 0) QuantityText = RemainingQuantity.ToString();
            if (!value) QuantityText = "0";
            PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
        }
    }
    public string QuantityText
    {
        get => _quantityText;
        set
        {
            var next = value ?? string.Empty;
            if (_quantityText == next) return;
            _quantityText = next;
            _quantity = int.TryParse(next, out var parsed) ? parsed : 0;
            PropertyChanged?.Invoke(this, new(nameof(QuantityText)));
            PropertyChanged?.Invoke(this, new(nameof(Quantity)));
            PropertyChanged?.Invoke(this, new(nameof(HasValidQuantity)));
        }
    }
    public int Quantity { get => _quantity; private set => QuantityText = value.ToString(); }
    public bool HasValidQuantity => int.TryParse(QuantityText, out _);
    public event PropertyChangedEventHandler? PropertyChanged;
}
