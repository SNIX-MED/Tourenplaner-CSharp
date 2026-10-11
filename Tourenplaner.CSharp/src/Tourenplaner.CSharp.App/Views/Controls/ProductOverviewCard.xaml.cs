using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tourenplaner.CSharp.App.Views.Controls;

public partial class ProductOverviewCard : UserControl
{
    public ProductOverviewCard() => InitializeComponent();

    public static readonly DependencyProperty TitleProperty = RegisterString(nameof(Title));
    public static readonly DependencyProperty SupplierProperty = RegisterString(nameof(Supplier));
    public static readonly DependencyProperty ExpectedDeliveryProperty = RegisterString(nameof(ExpectedDelivery));
    public static readonly DependencyProperty DimensionsLineProperty = RegisterString(nameof(DimensionsLine));
    public static readonly DependencyProperty WeightLineProperty = RegisterString(nameof(WeightLine));
    public static readonly DependencyProperty TotalLineProperty = RegisterString(nameof(TotalLine));
    public static readonly DependencyProperty DeliveryStatusProperty = RegisterString(nameof(DeliveryStatus));
    public static readonly DependencyProperty BackgroundColorProperty = RegisterBrush(nameof(BackgroundColor), Brushes.White);
    public static readonly DependencyProperty BorderColorProperty = RegisterBrush(nameof(BorderColor), Brushes.LightGray);
    public static readonly DependencyProperty IsSelectedProperty = RegisterBool(nameof(IsSelected));
    public static readonly DependencyProperty HasSupplierProperty = RegisterBool(nameof(HasSupplier));
    public static readonly DependencyProperty HasExpectedDeliveryProperty = RegisterBool(nameof(HasExpectedDelivery));
    public static readonly DependencyProperty HasDimensionsProperty = RegisterBool(nameof(HasDimensions));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Supplier { get => (string)GetValue(SupplierProperty); set => SetValue(SupplierProperty, value); }
    public string ExpectedDelivery { get => (string)GetValue(ExpectedDeliveryProperty); set => SetValue(ExpectedDeliveryProperty, value); }
    public string DimensionsLine { get => (string)GetValue(DimensionsLineProperty); set => SetValue(DimensionsLineProperty, value); }
    public string WeightLine { get => (string)GetValue(WeightLineProperty); set => SetValue(WeightLineProperty, value); }
    public string TotalLine { get => (string)GetValue(TotalLineProperty); set => SetValue(TotalLineProperty, value); }
    public string DeliveryStatus { get => (string)GetValue(DeliveryStatusProperty); set => SetValue(DeliveryStatusProperty, value); }
    public Brush BackgroundColor { get => (Brush)GetValue(BackgroundColorProperty); set => SetValue(BackgroundColorProperty, value); }
    public Brush BorderColor { get => (Brush)GetValue(BorderColorProperty); set => SetValue(BorderColorProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool HasSupplier { get => (bool)GetValue(HasSupplierProperty); set => SetValue(HasSupplierProperty, value); }
    public bool HasExpectedDelivery { get => (bool)GetValue(HasExpectedDeliveryProperty); set => SetValue(HasExpectedDeliveryProperty, value); }
    public bool HasDimensions { get => (bool)GetValue(HasDimensionsProperty); set => SetValue(HasDimensionsProperty, value); }

    private static DependencyProperty RegisterString(string name) =>
        DependencyProperty.Register(name, typeof(string), typeof(ProductOverviewCard), new PropertyMetadata(string.Empty));

    private static DependencyProperty RegisterBrush(string name, Brush fallback) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(ProductOverviewCard), new PropertyMetadata(fallback));

    private static DependencyProperty RegisterBool(string name) =>
        DependencyProperty.Register(name, typeof(bool), typeof(ProductOverviewCard), new PropertyMetadata(false));
}
