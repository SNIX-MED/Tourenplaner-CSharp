namespace Tourenplaner.CSharp.Tests.Application;

public sealed class CreateTourStaffingAccessibilityTests
{
    [Fact]
    public void InlineWarning_IsWrappedScrollableAndExposedAsLiveRegion()
    {
        var xaml = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Dialogs", "CreateTourDialogWindow.xaml");

        Assert.Contains("x:Name=\"EmployeeRecommendationWarning\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding EmployeeRecommendationAccessibleName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding EmployeeRecommendationMessage}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Confirmation_UsesSafeLabelsAndRequiresExplicitPrimaryFocus()
    {
        var service = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Services", "TourStaffingWarningService.cs");
        var dialog = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Dialogs", "AppMessageDialogWindow.xaml.cs");

        Assert.Contains("Zweiten Mitarbeiter prüfen", service, StringComparison.Ordinal);
        Assert.Contains("Zurück zur Auswahl", service, StringComparison.Ordinal);
        Assert.Contains("Trotzdem speichern", service, StringComparison.Ordinal);
        Assert.Contains("PrimaryButton.IsDefault = false", dialog, StringComparison.Ordinal);
        Assert.Contains("SecondaryButton.Focus()", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogPreventsRepeatedSubmissionAndKeepsInputsOpenWhenConfirmationIsCancelled()
    {
        var dialog = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Dialogs", "CreateTourDialogWindow.xaml.cs");

        Assert.Contains("if (_isSubmitting)", dialog, StringComparison.Ordinal);
        Assert.Contains("_isSubmitting = true;", dialog, StringComparison.Ordinal);
        Assert.Contains("SaveButton.IsEnabled = false;", dialog, StringComparison.Ordinal);
        Assert.Contains("!= MessageBoxResult.Yes)", dialog, StringComparison.Ordinal);
        Assert.Contains("return;", dialog, StringComparison.Ordinal);
        Assert.True(
            dialog.IndexOf("!= MessageBoxResult.Yes", StringComparison.Ordinal) <
            dialog.IndexOf("Result = result;", StringComparison.Ordinal),
            "A cancelled staffing confirmation must return before the dialog result is committed.");
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
