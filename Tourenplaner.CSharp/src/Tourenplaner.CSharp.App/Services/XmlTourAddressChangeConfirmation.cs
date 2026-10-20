using System.Windows;
using System.Windows.Controls;
using Tourenplaner.CSharp.Application.Common;

namespace Tourenplaner.CSharp.App.Services;

internal static class XmlTourAddressChangeConfirmation
{
    public static bool Show(IReadOnlyList<ImportTourAddressChange> changes)
    {
        var window = new Window
        {
            Title = "Lieferadressen eingeplanter Aufträge ändern", Width = 760, Height = 480,
            MinWidth = 500, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Application.Current?.MainWindow
        };
        var panel = new DockPanel { Margin = new Thickness(20) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, Padding = new Thickness(16, 8, 16, 8) };
        var confirm = new Button { Content = "Adressänderungen übernehmen", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(12, 0, 0, 0) };
        confirm.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        var text = "Das Fahrtziel bereits eingeplanter Stopps wird geändert. Die Stoppreihenfolge bleibt erhalten. " +
            "Die betroffenen Touren werden anschließend neu berechnet und zur Prüfung markiert.\n\n" +
            string.Join("\n\n", changes.Select(c => c.Description));
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14 } });
        window.Content = panel;
        return window.ShowDialog() == true;
    }
}
