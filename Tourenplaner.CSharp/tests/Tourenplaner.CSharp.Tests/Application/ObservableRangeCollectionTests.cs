using System.Collections.Specialized;
using Tourenplaner.CSharp.App.ViewModels.Sections;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class ObservableRangeCollectionTests
{
    [Fact]
    public void ReplaceAll_ReplacesContentsWithSingleResetNotification()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2 };
        var notifications = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => notifications.Add(args);

        collection.ReplaceAll([3, 4, 5]);

        Assert.Equal([3, 4, 5], collection);
        var notification = Assert.Single(notifications);
        Assert.Equal(NotifyCollectionChangedAction.Reset, notification.Action);
    }
}
