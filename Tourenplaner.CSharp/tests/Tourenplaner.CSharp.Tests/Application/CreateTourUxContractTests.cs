using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class CreateTourUxContractTests
{
    [Fact]
    public void NewTourDraft_UsesDetailsAction_WhileExistingTourStillUsesSave()
    {
        Assert.Equal("Tourdetails festlegen", KarteSectionViewModel.ResolveCurrentTourPrimaryActionText(isExistingTour: false));
        Assert.Equal("Speichern", KarteSectionViewModel.ResolveCurrentTourPrimaryActionText(isExistingTour: true));
    }

    [Fact]
    public void NewTourAction_OpensDetailsDialog_AndOnlyDialogConfirmationPersistsTour()
    {
        var mapXaml = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Sections", "KarteSectionView.xaml");
        var dialogXaml = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Dialogs", "CreateTourDialogWindow.xaml");
        var workflow = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "ViewModels", "Sections", "KarteSectionViewModel.RoutingAndDetails.cs");

        Assert.Contains("x:Name=\"CurrentTourPrimaryActionButton\"", mapXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding CurrentTourPrimaryActionText}\"", mapXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding CurrentTourPrimaryActionAccessibleName}\"", mapXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SaveCurrentTourCommand}\"", mapXaml, StringComparison.Ordinal);

        Assert.Contains("Text=\"Neue Liefertour\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SaveButton\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Speichern\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnCreateClicked\"", dialogXaml, StringComparison.Ordinal);

        var draftBranch = workflow.IndexOf("await OpenCreateTourDialogAsync();", StringComparison.Ordinal);
        var dialogOpen = workflow.IndexOf("if (dialog.ShowDialog() != true || dialog.Result is null)", draftBranch, StringComparison.Ordinal);
        var persist = workflow.IndexOf("await SaveRouteAsTourAsync(", dialogOpen, StringComparison.Ordinal);
        Assert.True(draftBranch >= 0, "The draft action must open the create-tour dialog.");
        Assert.True(dialogOpen > draftBranch, "The dialog must be shown before persistence.");
        Assert.True(persist > dialogOpen, "Persistence must happen only after the dialog returns a result.");
    }

    private static string ReadRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, Path.Combine(relativeSegments)));
    }
}
