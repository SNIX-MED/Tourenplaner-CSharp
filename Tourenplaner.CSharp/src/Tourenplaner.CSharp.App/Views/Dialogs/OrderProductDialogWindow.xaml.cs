using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        UnsavedChangesGuard.Attach(this, () => UnsavedChangesGuard.Capture(ViewModel), () => DialogResult == true);
    }

    public OrderProductDialogViewModel ViewModel { get; }

    public ProductLineInput? Result { get; private set; }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildResult(out var result, out var error))
        {
            Tourenplaner.CSharp.App.Services.AppMessageBox.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void OnUnitWeightPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = sender is not TextBox textBox || !IsValidWeightInput(BuildProspectiveText(textBox, e.Text));
    }

    private void OnUnitWeightPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox ||
            !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true) ||
            e.SourceDataObject.GetData(DataFormats.UnicodeText, true) is not string pastedText ||
            !IsValidWeightInput(BuildProspectiveText(textBox, pastedText)))
        {
            e.CancelCommand();
        }
    }

    private void OnDimensionPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = sender is not TextBox textBox || !IsValidDimensionInput(BuildProspectiveText(textBox, e.Text));
    }

    private void OnDimensionPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox ||
            !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true) ||
            e.SourceDataObject.GetData(DataFormats.UnicodeText, true) is not string pastedText ||
            !IsValidDimensionInput(BuildProspectiveText(textBox, pastedText)))
        {
            e.CancelCommand();
        }
    }

    private static bool IsValidDimensionInput(string text) => text.All(char.IsDigit);

    private static string BuildProspectiveText(TextBox textBox, string insertedText)
    {
        var currentText = textBox.Text ?? string.Empty;
        var selectionStart = Math.Clamp(textBox.SelectionStart, 0, currentText.Length);
        var selectionLength = Math.Clamp(textBox.SelectionLength, 0, currentText.Length - selectionStart);
        return currentText.Remove(selectionStart, selectionLength).Insert(selectionStart, insertedText);
    }

    private static bool IsValidWeightInput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var decimalSeparatorCount = 0;
        foreach (var character in text)
        {
            if (char.IsDigit(character))
            {
                continue;
            }

            if (character is ',' or '.')
            {
                decimalSeparatorCount++;
                if (decimalSeparatorCount <= 1)
                {
                    continue;
                }
            }

            return false;
        }

        return true;
    }
}

public sealed class OrderProductDialogViewModel : ObservableObject
{
    private readonly string _positionId;
    private readonly string _externalPositionId;
    private string _name = string.Empty;
    private string _supplier = string.Empty;
    private string _expectedDelivery = string.Empty;
    private string _quantityText = "1";
    private string _unitWeightKgText = string.Empty;
    private string _widthMmText = string.Empty;
    private string _depthMmText = string.Empty;
    private string _heightMmText = string.Empty;
    private string _effectiveUnitWeightKgText = string.Empty;
    private string _effectiveWidthMmText = string.Empty;
    private string _effectiveDepthMmText = string.Empty;
    private string _effectiveHeightMmText = string.Empty;
    private string _selectedDeliveryStatus = OrderProductInfo.DefaultDeliveryStatus;

    public OrderProductDialogViewModel(ProductLineInput? existingProduct = null)
    {
        _positionId = existingProduct?.PositionId ?? string.Empty;
        _externalPositionId = existingProduct?.ExternalPositionId ?? string.Empty;
        if (existingProduct is null)
        {
            return;
        }

        _name = existingProduct.Name;
        _supplier = existingProduct.Supplier;
        _expectedDelivery = existingProduct.ExpectedDelivery;
        _quantityText = OrderProductInfo.NormalizeQuantity(existingProduct.Quantity).ToString(CultureInfo.InvariantCulture);
        _unitWeightKgText = existingProduct.UnitWeightKg.ToString("0.###", CultureInfo.InvariantCulture);
        if (ProductDimensionsMm.TryParse(existingProduct.Dimensions, out var dimensions))
        {
            _widthMmText = dimensions.Width.ToString(CultureInfo.InvariantCulture);
            _depthMmText = dimensions.Depth.ToString(CultureInfo.InvariantCulture);
            _heightMmText = dimensions.Height.ToString(CultureInfo.InvariantCulture);
        }
        _effectiveUnitWeightKgText = existingProduct.EffectiveUnitWeightKg?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
        if (ProductDimensionsMm.TryParse(existingProduct.EffectiveDimensions, out var effectiveDimensions))
        {
            _effectiveWidthMmText = effectiveDimensions.Width.ToString(CultureInfo.InvariantCulture);
            _effectiveDepthMmText = effectiveDimensions.Depth.ToString(CultureInfo.InvariantCulture);
            _effectiveHeightMmText = effectiveDimensions.Height > 0
                ? effectiveDimensions.Height.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }
        _selectedDeliveryStatus = OrderProductInfo.NormalizeDeliveryStatus(existingProduct.DeliveryStatus);
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

    public string ExpectedDelivery
    {
        get => _expectedDelivery;
        set
        {
            if (SetProperty(ref _expectedDelivery, value))
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

    public string WidthMmText
    {
        get => _widthMmText;
        set
        {
            if (SetProperty(ref _widthMmText, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public string DepthMmText
    {
        get => _depthMmText;
        set
        {
            if (SetProperty(ref _depthMmText, value)) OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string HeightMmText
    {
        get => _heightMmText;
        set
        {
            if (SetProperty(ref _heightMmText, value)) OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string EffectiveUnitWeightKgText { get => _effectiveUnitWeightKgText; set => SetProperty(ref _effectiveUnitWeightKgText, value); }
    public string EffectiveWidthMmText { get => _effectiveWidthMmText; set => SetProperty(ref _effectiveWidthMmText, value); }
    public string EffectiveDepthMmText { get => _effectiveDepthMmText; set => SetProperty(ref _effectiveDepthMmText, value); }
    public string EffectiveHeightMmText { get => _effectiveHeightMmText; set => SetProperty(ref _effectiveHeightMmText, value); }

    public IReadOnlyList<string> DeliveryStatusOptions => OrderProductInfo.DeliveryStatusOptions;

    public string SelectedDeliveryStatus
    {
        get => _selectedDeliveryStatus;
        set
        {
            var normalized = OrderProductInfo.NormalizeDeliveryStatus(value);
            if (SetProperty(ref _selectedDeliveryStatus, normalized))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public string SummaryText
    {
        get
        {
            _ = TryParseNonZeroInt(QuantityText, out var quantity);
            _ = TryParseNonNegativeDouble(UnitWeightKgText, out var unitWeightKg);
            var totalWeightKg = quantity * unitWeightKg;
            var dimensionsText = TryParseDimensions(out var dimensions) ? dimensions.DisplayText : string.Empty;
            _ = TryParseOptionalEffectiveWeight(out var effectiveWeight);
            _ = TryParseOptionalEffectiveDimensions(out var effectiveDimensions);
            var product = new OrderProductInfo
            {
                Name = (Name ?? string.Empty).Trim(),
                Supplier = (Supplier ?? string.Empty).Trim(),
                ExpectedDelivery = (ExpectedDelivery ?? string.Empty).Trim(),
                Quantity = quantity,
                UnitWeightKg = unitWeightKg,
                WeightKg = totalWeightKg,
                Dimensions = dimensionsText,
                EffectiveUnitWeightKg = effectiveWeight,
                EffectiveDimensions = effectiveDimensions?.DisplayText ?? string.Empty,
                DeliveryStatus = OrderProductInfo.NormalizeDeliveryStatus(SelectedDeliveryStatus)
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

        if (!TryParseNonZeroInt(QuantityText, out var quantity))
        {
            error = "Bitte eine gültige Menge ungleich 0 eingeben.";
            return false;
        }

        if (!TryParseNonNegativeDouble(UnitWeightKgText, out var unitWeightKg))
        {
            error = "Bitte ein gültiges Gewicht pro Stück eingeben.";
            return false;
        }

        if (!TryParseDimensions(out var dimensions))
        {
            error = "Bitte Breite, Tiefe und Höhe als positive Ganzzahlen in Millimetern eingeben.";
            return false;
        }

        if (!TryParseOptionalEffectiveWeight(out var effectiveWeight))
        {
            error = "Bitte das effektive Gewicht als positive Zahl eingeben oder das Feld leeren.";
            return false;
        }
        if (!TryParseOptionalEffectiveDimensions(out var effectiveDimensions))
        {
            error = "Bei effektiven Verpackungsmaßen müssen Breite und Tiefe positiv sein; die Höhe ist optional.";
            return false;
        }

        result = new ProductLineInput
        {
            PositionId = _positionId,
            ExternalPositionId = _externalPositionId,
            Name = Name.Trim(),
            Supplier = (Supplier ?? string.Empty).Trim(),
            ExpectedDelivery = (ExpectedDelivery ?? string.Empty).Trim(),
            Quantity = quantity,
            UnitWeightKg = unitWeightKg,
            Dimensions = dimensions.DisplayText,
            EffectiveUnitWeightKg = effectiveWeight,
            EffectiveDimensions = effectiveDimensions?.DisplayText ?? string.Empty,
            DeliveryStatus = OrderProductInfo.NormalizeDeliveryStatus(SelectedDeliveryStatus)
        };
        return true;
    }

    private bool TryParseOptionalEffectiveWeight(out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(EffectiveUnitWeightKgText)) return true;
        if (!TryParseNonNegativeDouble(EffectiveUnitWeightKgText, out var parsed) || parsed <= 0) return false;
        value = parsed;
        return true;
    }

    private bool TryParseOptionalEffectiveDimensions(out ProductDimensionsMm? dimensions)
    {
        dimensions = null;
        var widthText = (EffectiveWidthMmText ?? string.Empty).Trim();
        var depthText = (EffectiveDepthMmText ?? string.Empty).Trim();
        var heightText = (EffectiveHeightMmText ?? string.Empty).Trim();
        if (widthText.Length == 0 && depthText.Length == 0 && heightText.Length == 0) return true;
        if (!int.TryParse(widthText, out var width) || width <= 0 ||
            !int.TryParse(depthText, out var depth) || depth <= 0) return false;
        if (heightText.Length > 0 && (!int.TryParse(heightText, out var parsedHeight) || parsedHeight <= 0)) return false;
        dimensions = new ProductDimensionsMm(width, depth, heightText.Length == 0 ? 0 : int.Parse(heightText));
        return true;
    }

    private bool TryParseDimensions(out ProductDimensionsMm dimensions)
    {
        dimensions = default;
        var widthText = (WidthMmText ?? string.Empty).Trim();
        var depthText = (DepthMmText ?? string.Empty).Trim();
        var heightText = (HeightMmText ?? string.Empty).Trim();
        if (widthText.Length == 0 && depthText.Length == 0 && heightText.Length == 0) return true;
        if (!int.TryParse(widthText, NumberStyles.None, CultureInfo.InvariantCulture, out var width) || width <= 0 ||
            !int.TryParse(depthText, NumberStyles.None, CultureInfo.InvariantCulture, out var depth) || depth <= 0 ||
            !int.TryParse(heightText, NumberStyles.None, CultureInfo.InvariantCulture, out var height) || height <= 0)
        {
            return false;
        }

        dimensions = new ProductDimensionsMm(width, depth, height);
        return true;
    }

    private static bool TryParseNonZeroInt(string? text, out int value)
    {
        var raw = (text ?? string.Empty).Trim();
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            value = 1;
            return false;
        }

        return value != 0;
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


