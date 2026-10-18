using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class AppDataHistoryInfrastructureTests
{
    [Theory]
    [InlineData(null, "A-1", "Auftrag erstellen")]
    [InlineData("A-1", null, "Auftrag löschen")]
    [InlineData("A-1", "A-1", "Auftrag aktualisieren")]
    [InlineData("A-1", "A-2", "Auftrag umbenennen")]
    [InlineData(null, null, "Aufträge ändern")]
    public void DescriptionFormatter_FormatsOrderActions(
        string? previousId,
        string? currentId,
        string expected)
    {
        var args = new AppDataChangedEventArgs(
            Guid.NewGuid(),
            AppDataKind.Orders,
            previousId,
            currentId);

        var description = AppDataHistoryDescriptionFormatter.Format(
            args,
            AppDataHistoryDescriptionProfile.LocalFile);

        Assert.Equal(expected, description);
    }

    [Fact]
    public void DescriptionFormatter_PreservesMediaSpecificCombinedFallbacks()
    {
        var args = new AppDataChangedEventArgs(
            Guid.NewGuid(),
            AppDataKind.Orders | AppDataKind.Vehicles);

        Assert.Equal(
            "Aufträge, Fahrzeuge",
            AppDataHistoryDescriptionFormatter.Format(
                args,
                AppDataHistoryDescriptionProfile.LocalFile));
        Assert.Equal(
            "Daten ändern",
            AppDataHistoryDescriptionFormatter.Format(
                args,
                AppDataHistoryDescriptionProfile.PostgreSql));
    }

    [Fact]
    public void DescriptionFormatter_RecognizesOnlyEstablishedGenericDescriptions()
    {
        Assert.True(AppDataHistoryDescriptionFormatter.IsGeneric(null));
        Assert.True(AppDataHistoryDescriptionFormatter.IsGeneric("Änderung"));
        Assert.True(AppDataHistoryDescriptionFormatter.IsGeneric(" dateiÄNDERUNG "));
        Assert.True(AppDataHistoryDescriptionFormatter.IsGeneric("Dateiumbenennung"));
        Assert.False(AppDataHistoryDescriptionFormatter.IsGeneric("Auftrag aktualisieren"));
    }

    [Fact]
    public async Task DebounceCoordinator_ExecutesOnlyLatestScheduledCallback()
    {
        var executions = 0;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new AsyncDebounceCoordinator(
            TimeSpan.FromMilliseconds(30),
            _ =>
            {
                Interlocked.Increment(ref executions);
                completion.TrySetResult();
                return Task.CompletedTask;
            });

        coordinator.Schedule();
        coordinator.Schedule();
        coordinator.Schedule();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(80);

        Assert.Equal(1, Volatile.Read(ref executions));
    }

    [Fact]
    public async Task DebounceCoordinator_CancelAndDisposePreventPendingCallbacks()
    {
        var executions = 0;
        var coordinator = new AsyncDebounceCoordinator(
            TimeSpan.FromMilliseconds(50),
            _ =>
            {
                Interlocked.Increment(ref executions);
                return Task.CompletedTask;
            });

        coordinator.Schedule();
        coordinator.CancelPending();
        coordinator.Schedule();
        coordinator.Dispose();
        coordinator.Schedule();
        await Task.Delay(150);

        Assert.Equal(0, Volatile.Read(ref executions));
    }
}
