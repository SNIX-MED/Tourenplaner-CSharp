using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class ColorPickerDialogWindow : Window
{
    private readonly string _fallbackHex;
    private bool _isUpdating;
    private Color _selectedColor;

    public ColorPickerDialogWindow(
        string title,
        string description,
        string currentHex,
        string fallbackHex)
    {
        InitializeComponent();

        _fallbackHex = NormalizeHexColor(fallbackHex, "#64748B");
        Title = string.IsNullOrWhiteSpace(title) ? "Farbe waehlen" : title.Trim();
        TitleTextBlock.Text = Title;
        DescriptionTextBlock.Text = string.IsNullOrWhiteSpace(description)
            ? "Passe die Farbe fuer dieses Element an."
            : description.Trim();
        PreviewHeadingTextBlock.Text = Title;

        ApplySelectedColor(ParseHexColor(currentHex, _fallbackHex));
    }

    public string SelectedColorHex { get; private set; } = "#64748B";

    private void OnSwatchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string hex)
        {
            return;
        }

        ApplySelectedColor(ParseHexColor(hex, _fallbackHex));
    }

    private void OnChannelSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating)
        {
            return;
        }

        ApplySelectedColor(Color.FromRgb(
            (byte)Math.Round(RedSlider.Value),
            (byte)Math.Round(GreenSlider.Value),
            (byte)Math.Round(BlueSlider.Value)));
    }

    private void OnChannelTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        if (!TryReadByte(RedValueTextBox.Text, out var red) ||
            !TryReadByte(GreenValueTextBox.Text, out var green) ||
            !TryReadByte(BlueValueTextBox.Text, out var blue))
        {
            return;
        }

        ApplySelectedColor(Color.FromRgb(red, green, blue));
    }

    private void OnHexTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        if (!TryParseHexColor(HexTextBox.Text, out var color))
        {
            return;
        }

        ApplySelectedColor(color);
    }

    private void OnResetClicked(object sender, RoutedEventArgs e)
    {
        ApplySelectedColor(ParseHexColor(_fallbackHex, "#64748B"));
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void ApplySelectedColor(Color color)
    {
        _selectedColor = color;
        SelectedColorHex = FormatHexColor(color);

        _isUpdating = true;
        try
        {
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            RedValueTextBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
            GreenValueTextBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
            BlueValueTextBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
            HexTextBox.Text = SelectedColorHex;
            SelectedHexBadgeTextBlock.Text = SelectedColorHex;

            var brush = new SolidColorBrush(color);
            PreviewSwatch.Background = brush;
            PreviewBadge.Background = brush;
            PreviewDot.Background = brush;
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private static bool TryReadByte(string? value, out byte result)
    {
        result = 0;
        return byte.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static string FormatHexColor(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static Color ParseHexColor(string? value, string fallbackHex)
    {
        return TryParseHexColor(value, out var color)
            ? color
            : TryParseHexColor(fallbackHex, out var fallbackColor)
                ? fallbackColor
                : Colors.SlateGray;
    }

    private static bool TryParseHexColor(string? value, out Color color)
    {
        var normalized = NormalizeHexColor(value, string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            color = Colors.Transparent;
            return false;
        }

        try
        {
            color = (Color)ColorConverter.ConvertFromString(normalized)!;
            return true;
        }
        catch
        {
            color = Colors.Transparent;
            return false;
        }
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 7 &&
            normalized.StartsWith('#') &&
            normalized.Skip(1).All(Uri.IsHexDigit))
        {
            return normalized.ToUpperInvariant();
        }

        return fallback;
    }
}
