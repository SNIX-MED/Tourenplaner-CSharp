using System.Windows;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class AdditionalMaterialItemDialogWindow : Window
{
    public AdditionalMaterialItemDialogWindow(MaterialItemEditor source)
    {
        InitializeComponent();
        Item = new MaterialItemEditor
        {
            Id = source.Id,
            Name = source.Name,
            QuantityText = source.QuantityText,
            WeightText = source.WeightText,
            RequiresLoadingSpace = source.RequiresLoadingSpace,
            WidthText = source.WidthText,
            DepthText = source.DepthText,
            HeightText = source.HeightText
        };
        DataContext = Item;
    }

    public MaterialItemEditor Item { get; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Item.Name))
        {
            Services.AppMessageBox.Show(this, "Bitte eine Bezeichnung eingeben.", "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!Item.TryGetQuantity(out _))
        {
            Services.AppMessageBox.Show(this, "Die Menge muss eine positive ganze Zahl sein.", "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!Item.TryGetWeight(out _))
        {
            Services.AppMessageBox.Show(this, "Das Gewicht muss eine positive Zahl oder 0 sein.", "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
