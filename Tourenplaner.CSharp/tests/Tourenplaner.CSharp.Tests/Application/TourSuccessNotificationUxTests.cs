namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourSuccessNotificationUxTests
{
    [Fact]
    public void Toast_ExposesFullTextWithoutMovingFocus()
    {
        var xaml = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "MainWindow.xaml");

        Assert.Contains("ToolTip=\"{Binding ToastMessage}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding ToastMessage}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding ToastMessage}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Focus()", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessNotifications_AreCreatedOnlyAfterPersistenceCompletes()
    {
        var workflow = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "ViewModels", "Sections", "KarteSectionViewModel.RoutingAndDetails.cs");
        var saveCall = workflow.IndexOf("await SaveChangedToursAndOrdersAsync", StringComparison.Ordinal);
        var committed = workflow.IndexOf("wasCommitted = true;", saveCall, StringComparison.Ordinal);
        var message = workflow.IndexOf("FormatTourCreatedMessage(tour)", committed, StringComparison.Ordinal);
        var toast = workflow.IndexOf("ToastNotificationService.ShowInfo(successMessage)", message, StringComparison.Ordinal);
        var refresh = workflow.IndexOf("await RefreshAfterLocalMutationAsync(nextId)", toast, StringComparison.Ordinal);

        Assert.True(saveCall >= 0);
        Assert.True(committed > saveCall);
        Assert.True(message > committed);
        Assert.True(toast > message);
        Assert.True(refresh > toast);
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
