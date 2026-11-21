using System.Windows;
using System.Windows.Input;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class LoadPlanPackageDialogWindow : Window
{
    public LoadPlanPackageDialogWindow(string name = "", string packageType = "Palette",
        int widthMm = 0, int depthMm = 0, int heightMm = 0)
    {
        InitializeComponent();
        NameBox.Text = name;
        TypeBox.Text = packageType;
        WidthBox.Text = widthMm > 0 ? widthMm.ToString() : string.Empty;
        DepthBox.Text = depthMm > 0 ? depthMm.ToString() : string.Empty;
        HeightBox.Text = heightMm > 0 ? heightMm.ToString() : string.Empty;
    }

    public string PackageName { get; private set; } = string.Empty;
    public string PackageType { get; private set; } = string.Empty;
    public int WidthMm { get; private set; }
    public int DepthMm { get; private set; }
    public int HeightMm { get; private set; }

    private void OnNumericInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(character => !char.IsDigit(character));

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || !int.TryParse(WidthBox.Text, out var width) || width <= 0 ||
            !int.TryParse(DepthBox.Text, out var depth) || depth <= 0 ||
            (!string.IsNullOrWhiteSpace(HeightBox.Text) &&
             (!int.TryParse(HeightBox.Text, out var parsedHeight) || parsedHeight <= 0)))
        {
            ErrorText.Text = "Bitte Bezeichnung, Breite und Tiefe eingeben. Die optionale Höhe muss ein positiver Millimeterwert sein.";
            return;
        }
        var height = string.IsNullOrWhiteSpace(HeightBox.Text) ? 0 : int.Parse(HeightBox.Text);
        PackageName = NameBox.Text.Trim();
        PackageType = TypeBox.Text.Trim();
        WidthMm = width; DepthMm = depth; HeightMm = height;
        DialogResult = true;
    }
}
