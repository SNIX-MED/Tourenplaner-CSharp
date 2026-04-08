using System.Globalization;
using System.Windows;
using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class OrderProductDialogWindow : Window
{
    public OrderProductDialogWindow(ProductLineInput? existingProduct = null)
    {
        InitializeComponent();
        ViewModel = new OrderProductDialogViewModel(existingProduct);
        DataContext = ViewModel;
    }

    public OrderProductDialogViewModel ViewModel { get; }

    public ProductLineInput? Result { get; private set; }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildResult(out var result, out var error))
        {
            MessageBox.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
        Close();
    }
}

public sealed class OrderProductDialogViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string _supplier = string.Empty;
    private string _quantityText = "1";
    private string _unitWeightKgText = string.Empty;
    private string _dimensions = string.Empty;

    public OrderProductDialogViewModel(ProductLineInput? existingProduct = null)
    {
        if (existingProduct is null)
        {
            return;
        }

        _name = existingProduct.Name;
        _supplier = existingProduct.Supplier;
        _quantityText = Math.Max(1, existingProduct.Quantity).ToString(CultureInfo.InvariantCulture);
        _unitWeightKgText = existingProduct.UnitWeightKg.ToString("0.###", CultureInfo.InvariantCulture);
        _dimensions = existingProduct.Dimensions;
    }

    public string Heading => string.IsNullOrWhiteSpace(Name) ? "Produkt erfassen" : "Produkt bearbeiten";

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(Heading));
                OnPropertyChanged(nameof(SummaryText));
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
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public string QuantityText
    {
        get => _quantityText;
        set
        {
            if (SetProperty(ref _quantityText, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public string UnitWeightKgText
    {
        get => _unitWeightKgText;
        set
        {
            if (SetProperty(ref _unitWeightKgText, value))
            {
                OnPropertyChanged(nameof(SummaryText));
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
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public string SummaryText
    {
        get
        {
            _ = TryParsePositiveInt(QuantityText, out var quantity);
            _ = TryParseNonNegativeDouble(UnitWeightKgText, out var unitWeightKg);
            var totalWeightKg = quantity * unitWeightKg;
            var product = new OrderProductInfo
            {
                Name = (Name ?? string.Empty).Trim(),
                Supplier = (Supplier ?? string.Empty).Trim(),
                Quantity = quantity,
                UnitWeightKg = unitWeightKg,
                WeightKg = totalWeightKg,
                Dimensions = (Dimensions ?? string.Empty).Trim()
            };
            return OrderProductFormatter.BuildDetails([product]);
        }
    }

    public bool TryBuildResult(out ProductLineInput result, out string error)
    {
        error = string.Empty;
        result = default!;

        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "Bitte einen Produktnamen eingeben.";
            return false;
        }

        if (!TryParsePositiveInt(QuantityText, out var quantity))
        {
            error = "Bitte eine gültige Menge größer oder gleich 1 eingeben.";
            return false;
        }

        if (!TryParseNonNegativeDouble(UnitWeightKgText, out var unitWeightKg))
        {
            error = "Bitte ein gültiges Gewicht pro Stück eingeben.";
            return false;
        }

        result = new ProductLineInput
        {
            Name = Name.Trim(),
            Supplier = (Supplier ?? string.Empty).Trim(),
            Quantity = quantity,
            UnitWeightKg = unitWeightKg,
            Dimensions = (Dimensions ?? string.Empty).Trim()
        };
        return true;
    }

    private static bool TryParsePositiveInt(string? text, out int value)
    {
        var raw = (text ?? string.Empty).Trim();
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            value = 1;
            return false;
        }

        value = Math.Max(1, value);
        return true;
    }

    private static bool TryParseNonNegativeDouble(string? text, out double value)
    {
        var raw = (text ?? string.Empty).Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            value = 0d;
            return false;
        }

        value = Math.Max(0d, value);
        return true;
    }
}
