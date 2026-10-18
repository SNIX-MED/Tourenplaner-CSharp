using System.Globalization;
using Tourenplaner.CSharp.App.Resources;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class GermanDateLocalizationTests
{
    [Fact]
    public void GermanDateResources_UseLocalizedDisplayHintAndReadableAccessibilityText()
    {
        Assert.Equal("TT.MM.JJJJ", GermanDateUiText.FormatHint);
        Assert.Equal("Datum (TT.MM.JJJJ)", GermanDateUiText.FieldLabel);
        Assert.Equal("Datum, Format Tag Punkt Monat Punkt Jahr", GermanDateUiText.AccessibleName);
        Assert.Contains("TT.MM.JJJJ", GermanDateUiText.InvalidTourDate, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAndEditTourDialog_UseTheSameLocalizedDateLabel()
    {
        var xaml = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Dialogs", "CreateTourDialogWindow.xaml");
        var codeBehind = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Dialogs", "CreateTourDialogWindow.xaml.cs");

        Assert.Contains("GermanDateUiText.FieldLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Title = \"Liefertour bearbeiten\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DialogHeading.Text = \"Liefertour bearbeiten\"", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedDatePicker_ExposesReadableGermanAccessibilityAndKeepsCalendarSelectionFormat()
    {
        var xaml = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Controls", "ModernDatePickerField.xaml");
        var codeBehind = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Controls", "ModernDatePickerField.xaml.cs");

        Assert.Contains("GermanDateUiText.AccessibleName", xaml, StringComparison.Ordinal);
        Assert.Contains("GermanDateUiText.HelpText", xaml, StringComparison.Ordinal);
        Assert.Contains("day.Date.ToString(\"dd.MM.yyyy\", CultureInfo.InvariantCulture)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void TourDateValidation_AcceptsValidDateAndPreservesPersistedValue()
    {
        var date = DateTime.Today.AddDays(7).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var model = CreateValidModel(date);

        Assert.True(model.TryBuildResult(out var result, out var error), error);
        Assert.Equal(date, result.RouteDate);
    }

    [Fact]
    public void TourDateValidation_RejectsInvalidDateWithLocalizedHint()
    {
        var model = CreateValidModel(DateTime.Today.AddDays(7).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
        model.DateText = "32.13.2026";

        Assert.False(model.TryBuildResult(out _, out var error));
        Assert.Equal(GermanDateUiText.InvalidTourDate, error);
    }

    [Fact]
    public void TechnicalDateFormatsRemainDotNetFormatsAndAreNeverLocalizedTokens()
    {
        var control = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Controls", "ModernDatePickerField.xaml.cs");
        var dialog = ReadRepositoryFile("src", "Tourenplaner.CSharp.App", "Views", "Dialogs", "CreateTourDialogWindow.xaml.cs");

        Assert.Contains("\"dd.MM.yyyy\"", control, StringComparison.Ordinal);
        Assert.Contains("\"dd.MM.yyyy\"", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("TryParseExact(normalizedDateText, GermanDateUiText", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void GermanAppSources_ContainNoLegacyEnglishDatePlaceholder()
    {
        var appRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(appRoot.FullName, "src", "Tourenplaner.CSharp.App");
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("DD.MM.YYYY", content, StringComparison.Ordinal);
            Assert.DoesNotContain("DD/MM/YYYY", content, StringComparison.Ordinal);
        }
    }

    private static CreateTourDialogViewModel CreateValidModel(string date)
    {
        var model = new CreateTourDialogViewModel(
            date,
            "Testtour",
            "08",
            "00",
            [new TourLookupOption("vehicle", "Fahrzeug")],
            [],
            [new TourEmployeeOption("employee", "Mitarbeiter", false)]);
        model.SelectedVehicle = model.VehicleOptions.Single(option => option.Id == "vehicle");
        model.Employees.Single().IsSelected = true;
        return model;
    }

    private static string ReadRepositoryFile(params string[] relativeSegments) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, Path.Combine(relativeSegments)));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!;
    }
}
