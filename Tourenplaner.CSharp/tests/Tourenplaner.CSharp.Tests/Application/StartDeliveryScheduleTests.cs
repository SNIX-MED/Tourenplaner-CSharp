using Tourenplaner.CSharp.App.ViewModels.Sections;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class StartDeliveryScheduleTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    [Fact]
    public void Dashboard_MixesFixedDatesAndPeriodsInChronologicalOrder()
    {
        var orders = new[]
        {
            Period("WEEK", "KW 43"),
            Fixed("FIXED", new DateOnly(2026, 10, 18)),
            Period("OPEN", "Ab 31.10.2026"),
            Period("ONGOING", "Oktober 2026"),
            Period("RANGE", "16.–17.10.2026"),
            Period("DEADLINE", "bis 20.10.2026")
        };

        var items = StartSectionViewModel.CreateFixedDateItems(orders, Today);

        Assert.Equal(new[] { "ONGOING", "RANGE", "FIXED", "WEEK", "DEADLINE", "OPEN" }, items.Select(x => x.OrderNumber));
        Assert.Equal("01.10.2026 – 31.10.2026", items[0].DateText);
        Assert.True(items[0].IsToday);
        Assert.Equal("Oktober 2026", items[0].DeliveryPeriod);
        Assert.Equal("Ab 31.10.2026", items[^1].DateText);
        Assert.False(items[^1].IsToday);
        Assert.All(orders.Where(x => x.Id != "FIXED"), order => Assert.Null(order.DeliveryDate));
    }

    [Fact]
    public void Dashboard_KeepsOngoingAndOpenPeriodsButHidesExpiredAndUnclearOnes()
    {
        var items = StartSectionViewModel.CreateFixedDateItems(
        [
            Period("EXPIRED", "KW3 bis KW6"),
            Period("UNKNOWN", "nach Absprache"),
            Period("INVALID", "31.02.2026"),
            Period("OLD_OPEN", "ab 01.01.2025"),
            Period("END_TODAY", "13.–15.10.2026"),
            Period("DEADLINE_TODAY", "bis 15.10.2026"),
            Period("PAST_DEADLINE", "bis 14.10.2026"),
            Fixed("TODAY", Today),
            Fixed("PAST", Today.AddDays(-1))
        ], Today);

        Assert.Equal(new[] { "OLD_OPEN", "END_TODAY", "DEADLINE_TODAY", "TODAY" }, items.Select(x => x.OrderNumber));
        Assert.All(items, item => Assert.True(item.IsToday));
        Assert.Equal("Heute", items[^1].DateText);
    }

    [Fact]
    public void Dashboard_ConcreteDeliveryDateTakesPrecedenceAndRetainsEarlierDeliveryRule()
    {
        var fixedOrder = Fixed("FIXED", new DateOnly(2026, 11, 1));
        fixedOrder.DeliveryPeriod = "KW43";
        var past = Fixed("PAST", Today.AddDays(-1));
        past.DeliveryPeriod = "KW51";
        var earlier = Fixed("EARLIER", Today.AddDays(1));
        earlier.DeliveryCanOccurEarlier = true;
        earlier.DeliveryPeriod = "KW51";
        var archived = Period("ARCHIVED", "KW51");
        archived.IsArchived = true;
        var minDate = Period("MIN", "KW43");
        minDate.DeliveryDate = DateOnly.MinValue;

        var items = StartSectionViewModel.CreateFixedDateItems([fixedOrder, past, earlier, archived, minDate], Today);

        Assert.Equal(new[] { "MIN", "FIXED" }, items.Select(x => x.OrderNumber));
        Assert.Contains("01.11.2026", items[1].DateText);
    }

    [Fact]
    public void Dashboard_RebuildUsesNewTextAndKeepsTiesStable()
    {
        var first = Period("B", "KW43");
        var second = Period("A", "KW43");
        var items = StartSectionViewModel.CreateFixedDateItems([first, second], Today);
        Assert.Equal(new[] { "A", "B" }, items.Select(x => x.OrderNumber));

        second.DeliveryPeriod = "KW51";
        var refreshed = StartSectionViewModel.CreateFixedDateItems([first, second], Today);
        Assert.Equal(new[] { "B", "A" }, refreshed.Select(x => x.OrderNumber));
        Assert.Contains("14.12.2026", refreshed[1].DateText);
    }

    private static Order Period(string id, string text) => new()
    {
        Id = id, CustomerName = "Kunde", DeliveryPeriod = text
    };

    private static Order Fixed(string id, DateOnly date) => new()
    {
        Id = id, CustomerName = "Kunde", DeliveryDate = date
    };
}
