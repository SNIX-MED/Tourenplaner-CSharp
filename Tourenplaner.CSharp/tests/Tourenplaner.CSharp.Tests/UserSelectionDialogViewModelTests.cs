using System.Windows;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.Tests;

public sealed class UserSelectionDialogViewModelTests
{
    [Fact]
    public void EmptyInstallationAllowsEnteringFirstUser()
    {
        var model = new UserSelectionDialogViewModel([], null);

        Assert.Equal(Visibility.Visible, model.CreationVisibility);
        Assert.Equal(Visibility.Collapsed, model.SelectionVisibility);
        Assert.Empty(model.SelectedUserName);
        model.SelectedUserName = "Anna ";
        Assert.Equal("Anna ", model.SelectedUserName);
        model.SelectedUserName += "Muster";
        Assert.Equal("Anna Muster", model.SelectedUserName);
    }

    [Fact]
    public void ExistingProfilesKeepSelectionAndPreferredUser()
    {
        var model = new UserSelectionDialogViewModel(["Anna", "Beat"], "Beat");

        Assert.Equal(Visibility.Collapsed, model.CreationVisibility);
        Assert.Equal(Visibility.Visible, model.SelectionVisibility);
        Assert.Equal("Beat", model.SelectedUserName);
    }
}
