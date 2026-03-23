using System.Windows;
using System.Windows.Controls;

namespace Tourenplaner.CSharp.App.Views.Components;

public partial class KpiCardControl : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(KpiCardControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(KpiCardControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TrendProperty =
        DependencyProperty.Register(nameof(Trend), typeof(string), typeof(KpiCardControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(KpiCardControl), new PropertyMetadata(string.Empty));

    public KpiCardControl()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Trend
    {
        get => (string)GetValue(TrendProperty);
        set => SetValue(TrendProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
