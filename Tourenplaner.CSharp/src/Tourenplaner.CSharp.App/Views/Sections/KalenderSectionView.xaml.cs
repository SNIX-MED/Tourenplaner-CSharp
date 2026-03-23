using System.Windows.Controls;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class KalenderSectionView : UserControl
{
    public KalenderSectionView()
    {
        InitializeComponent();
    }

    private void CalendarDayList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not KalenderSectionViewModel viewModel)
        {
            return;
        }

        viewModel.HandleDayDoubleClick();
    }
}
