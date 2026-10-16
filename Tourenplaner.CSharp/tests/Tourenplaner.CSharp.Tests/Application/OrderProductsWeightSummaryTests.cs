using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class OrderProductsWeightSummaryTests
{
    [Fact]
    public void TotalIncludesQuantitiesAndUpdatesWhenProductsAreChangedOrRemoved()
    {
        var editor = new ManualOrderDialogViewModel();
        var notifications = new List<string?>();
        editor.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        var product = new ProductLineInput { Name = "Produkt", Quantity = 3, UnitWeightKg = 12.5 };
        editor.AddProductLine(product);
        Assert.Contains((37.5).ToString("0.##"), editor.ProductsTotalWeightText);
        Assert.Contains(nameof(editor.ProductsTotalWeightText), notifications);
        notifications.Clear();
        product.Quantity = 4;
        Assert.Equal("Gesamtgewicht aller Produkte: 50 kg", editor.ProductsTotalWeightText);
        Assert.Contains(nameof(editor.ProductsTotalWeightText), notifications);
        product.UnitWeightKg = 10;
        editor.AddProductLine(new ProductLineInput { Name = "Zweites Produkt", Quantity = 2, UnitWeightKg = 5 });
        editor.ManualTotalWeightKgText = "1000";
        Assert.Equal("Gesamtgewicht aller Produkte: 50 kg", editor.ProductsTotalWeightText);
        editor.SelectedProductLine = product;
        editor.RemoveSelectedProductLine();
        Assert.Equal("Gesamtgewicht aller Produkte: 10 kg", editor.ProductsTotalWeightText);
    }
}
