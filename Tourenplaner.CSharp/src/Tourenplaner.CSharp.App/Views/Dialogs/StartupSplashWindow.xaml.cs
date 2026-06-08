using System.Windows;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class StartupSplashWindow : Window
{
    public StartupSplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }
}
