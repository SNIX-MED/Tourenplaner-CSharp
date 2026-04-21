using System.Windows;
using System.Windows.Media;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class AppMessageDialogWindow : Window
{
    public AppMessageDialogWindow(string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();

        CaptionTextBlock.Text = string.IsNullOrWhiteSpace(caption) ? "Hinweis" : caption.Trim();
        MessageTextBlock.Text = message ?? string.Empty;
        Title = CaptionTextBlock.Text;

        ConfigureVisuals(image);
        ConfigureButtons(buttons);
    }

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private void ConfigureButtons(MessageBoxButton buttons)
    {
        if (buttons == MessageBoxButton.YesNo)
        {
            SecondaryButton.Visibility = Visibility.Visible;
            SecondaryButton.Content = "Nein";
            PrimaryButton.Content = "Ja";
            PrimaryButton.Style = (Style)FindResource("SuccessButtonStyle");
            return;
        }

        SecondaryButton.Visibility = Visibility.Collapsed;
        PrimaryButton.Content = "OK";
        PrimaryButton.Style = (Style)FindResource("PrimaryButtonStyle");
    }

    private void ConfigureVisuals(MessageBoxImage image)
    {
        var glyph = "i";
        var badgeBrushKey = "Brush.AccentSoft";
        var borderBrushKey = "Brush.BorderSoft";

        if (image == MessageBoxImage.Warning || image == MessageBoxImage.Exclamation)
        {
            glyph = "!";
            badgeBrushKey = "Brush.DangerSoft";
            borderBrushKey = "Brush.Danger";
        }
        else if (image == MessageBoxImage.Error || image == MessageBoxImage.Hand || image == MessageBoxImage.Stop)
        {
            glyph = "x";
            badgeBrushKey = "Brush.DangerSoft";
            borderBrushKey = "Brush.Danger";
        }
        else if (image == MessageBoxImage.Question)
        {
            glyph = "?";
            badgeBrushKey = "Brush.AccentSoft";
            borderBrushKey = "Brush.Accent";
        }

        IconGlyphText.Text = glyph;
        if (TryFindResource(badgeBrushKey) is Brush badge)
        {
            IconBadge.Background = badge;
        }

        if (TryFindResource(borderBrushKey) is Brush border)
        {
            IconBadge.BorderBrush = border;
        }
    }

    private void OnPrimaryClicked(object sender, RoutedEventArgs e)
    {
        Result = SecondaryButton.Visibility == Visibility.Visible
            ? MessageBoxResult.Yes
            : MessageBoxResult.OK;
        DialogResult = true;
        Close();
    }

    private void OnSecondaryClicked(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        DialogResult = false;
        Close();
    }
}
