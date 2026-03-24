using System.Windows.Controls;
using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.App.Views.Sections;

public partial class ToursSectionView : UserControl
{
    public ToursSectionView()
    {
        InitializeComponent();
    }

    private void OnToursGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ToursSectionViewModel vm)
        {
            return;
        }

        if (vm.EditTourOnMapCommand.CanExecute(null))
        {
            vm.EditTourOnMapCommand.Execute(null);
        }
    }
}
