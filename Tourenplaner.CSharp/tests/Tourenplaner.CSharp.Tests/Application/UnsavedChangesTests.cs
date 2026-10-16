using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.Views.Dialogs;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class UnsavedChangesTests
{
    [Fact]
    public void TrackerDetectsInvalidInputAndRecognizesRevertedAndSavedChanges()
    {
        var editor = new CreateTourDialogViewModel(DateTime.Today.ToString("dd.MM.yyyy"), "", "08", "00", [], [], []);
        var tracker = new UnsavedChangesTracker(() => UnsavedChangesGuard.Capture(editor));
        Assert.False(tracker.HasChanges);
        var originalDate = editor.DateText;
        editor.DateText = "invalid";
        Assert.True(tracker.HasChanges);
        editor.DateText = originalDate;
        Assert.False(tracker.HasChanges);
        editor.Name = "Neue Tour";
        Assert.True(tracker.HasChanges);
        tracker.AcceptChanges();
        Assert.False(tracker.HasChanges);
    }

    [Fact]
    public void OrderTrackingIgnoresSelectionButIncludesProductsAndIncompleteAddress()
    {
        var editor = new ManualOrderDialogViewModel();
        string Capture() => UnsavedChangesGuard.Capture(editor, new
        {
            Products = editor.ProductLines.Select(x => UnsavedChangesGuard.Capture(x)).ToArray(),
            DeliveryNotes = editor.DeliveryNotes.Select(x => new { x.DeliveryId, x.Notes }).ToArray()
        }, nameof(ManualOrderDialogViewModel.IsEditingEnabled), nameof(ManualOrderDialogViewModel.SelectedProductDeliveryStatus));
        var tracker = new UnsavedChangesTracker(Capture);
        Assert.False(tracker.HasChanges);
        editor.DeliveryName = "Unfertiger Auftrag";
        Assert.True(tracker.HasChanges);
        editor.DeliveryName = "";
        Assert.False(tracker.HasChanges);
        editor.AddProductLine(new ProductLineInput { Name = "Produkt", Quantity = 1 });
        Assert.True(tracker.HasChanges);
        tracker.AcceptChanges();
        editor.SelectedProductLine = editor.ProductLines.Single();
        Assert.False(tracker.HasChanges);
        editor.ProductLines.Single().Quantity = 2;
        Assert.True(tracker.HasChanges);
    }

    [Fact]
    public void AbsenceTrackingIncludesNestedInvalidDatesAndRemoval()
    {
        var editor = new UnavailabilityEditorDialogViewModel([]);
        var tracker = new UnsavedChangesTracker(() => System.Text.Json.JsonSerializer.Serialize(
            editor.Periods.Select(x => UnsavedChangesGuard.Capture(x)).ToArray()));
        editor.AddPeriod();
        Assert.True(tracker.HasChanges);
        tracker.AcceptChanges();
        editor.Periods.Single().StartDate = "invalid";
        Assert.True(tracker.HasChanges);
        editor.Periods.Clear();
        Assert.True(tracker.HasChanges);
    }
}
