using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderDeliveryDateDisplayServiceTests
{
    [Theory]
    [InlineData(false, false, "planned")]
    [InlineData(true, false, "confirmed")]
    [InlineData(true, true, "fixed")]
    public void CardDate_ConfirmedTourUsesGreenConfirmationExceptForFixedDates(bool tourConfirmed, bool fixedDate, string kind)
    {
        var order = new Order { AssignedTourId = "10", AvisoStatus = "Bestätigt",
            DeliveryDate = fixedDate ? new DateOnly(2026, 10, 29) : null };
        var result = OrderDeliveryDateDisplayService.BuildCardDate(order,
            [new TourRecord { Id = 10, Date = "29.10.2026" }], tourConfirmed: tourConfirmed);
        Assert.Equal("29.10.2026", result.Text);
        Assert.Equal(kind, result.Kind);
    }

    [Fact]
    public void CardDate_ConfirmedTourDoesNotConfirmDifferentExplicitDeliveryDate()
    {
        var order = new Order { AssignedTourId = "10", DeliveryDate = new DateOnly(2026, 10, 30), DeliveryCanOccurEarlier = true };
        var result = OrderDeliveryDateDisplayService.BuildCardDate(order,
            [new TourRecord { Id = 10, Date = "29.10.2026" }], tourConfirmed: true);
        Assert.Equal("planned", result.Kind);
    }

    [Theory]
    [InlineData(false, "nicht avisiert", "fixed")]
    [InlineData(false, "Bestätigt", "fixed")]
    [InlineData(true, "nicht avisiert", "planned")]
    public void CardDate_UsesFixedDateContract(bool earlier, string aviso, string expectedKind)
    {
        var order = new Order { DeliveryDate = new DateOnly(2026, 10, 29), DeliveryCanOccurEarlier = earlier, AvisoStatus = aviso };
        var result = OrderDeliveryDateDisplayService.BuildCardDate(order, []);
        Assert.Equal("29.10.2026", result.Text);
        Assert.Equal(expectedKind, result.Kind);
    }

    [Theory]
    [InlineData("nicht avisiert", "provisional")]
    [InlineData("Bestätigt", "planned")]
    [InlineData("Bestaetigt", "planned")]
    public void CardDate_TourConfirmationNeverCreatesFixedDate(string aviso, string expectedKind)
    {
        var order = new Order { AssignedTourId = "10", AvisoStatus = aviso };
        var result = OrderDeliveryDateDisplayService.BuildCardDate(order, [new TourRecord { Id = 10, Date = "29.10.2026" }]);
        Assert.Equal("29.10.2026", result.Text);
        Assert.Equal(expectedKind, result.Kind);
    }

    [Fact]
    public void CardDate_PartialDeliveryUsesOwnTourAndDoesNotInheritDifferentFixedDate()
    {
        var order = new Order { DeliveryDate = new DateOnly(2026, 10, 28), AssignedTourId = "10", AvisoStatus = "Bestätigt" };
        var delivery = new OrderDelivery { PlannedDeliveryDate = new DateOnly(2026, 10, 29), IsPlannedDeliveryDateExplicit = true, AssignedTourId = "11" };
        var result = OrderDeliveryDateDisplayService.BuildCardDate(order,
            [new TourRecord { Id = 10, Date = "28.10.2026" }, new TourRecord { Id = 11, Date = "29.10.2026" }], delivery);
        Assert.Equal("29.10.2026", result.Text);
        Assert.Equal("planned", result.Kind);
    }

    [Fact]
    public void CardDate_MissingDateRemainsEmptyDespiteCreationDateOrPeriod()
    {
        var order = new Order { ScheduledDate = new DateOnly(2026, 10, 29), DeliveryDate = DateOnly.MinValue, DeliveryPeriod = "KW 44/2026" };
        Assert.Equal(new OrderDeliveryDateDisplayService.CardDate("", ""), OrderDeliveryDateDisplayService.BuildCardDate(order, []));
    }

    [Fact]
    public void DeliveryWithoutDateNeverFallsBackToOrderCreationDate()
    {
        var order = new Order { ScheduledDate = new DateOnly(2026, 9, 17), AssignedTourId = "10" };
        var delivery = new OrderDelivery();
        Assert.Empty(OrderDeliveryDateDisplayService.BuildDisplayText(order,
            [new TourRecord { Id = 10, Date = "20.10.2026" }], delivery));
    }

    [Fact]
    public void DeliveryUsesItsOwnTourWithPlanningSuffixAndKeepsExplicitDates()
    {
        var order = new Order { ScheduledDate = new DateOnly(2026, 9, 17), AssignedTourId = "10" };
        var delivery = new OrderDelivery { AssignedTourId = "11" };
        TourRecord[] tours = [new() { Id = 10, Date = "20.10.2026" }, new() { Id = 11, Date = "21.10.2026" }];
        Assert.Equal("21.10.2026 · Provisorisch", OrderDeliveryDateDisplayService.BuildDisplayText(order, tours, delivery));
        order.DeliveryDate = new DateOnly(2026, 10, 22);
        Assert.Equal("22.10.2026", OrderDeliveryDateDisplayService.BuildDisplayText(order, tours, delivery));
        delivery.PlannedDeliveryDate = new DateOnly(2026, 10, 23);
        Assert.Equal("23.10.2026", OrderDeliveryDateDisplayService.BuildDisplayText(order, tours, delivery));
    }

    [Fact]
    public void RepairedLegacyDateDisappearsFromDisplay()
    {
        var order = new Order { ScheduledDate = new DateOnly(2026, 9, 17), Products = [] };
        var delivery = new OrderDelivery { Id = "D1", SequenceNumber = 1, PlannedDeliveryDate = order.ScheduledDate };
        order.Deliveries = [delivery];
        new Tourenplaner.CSharp.Application.Services.OrderDeliveryService().Normalize(order);
        Assert.Empty(OrderDeliveryDateDisplayService.BuildDisplayText(order, [], delivery));
    }

    [Fact]
    public void BuildDisplayText_LeavesMissingDeliveryDateEmptyWhenOrderIsNotAssigned()
    {
        var order = new Order
        {
            Id = "A-1",
            DeliveryDate = null,
            AssignedTourId = string.Empty
        };

        var displayText = OrderDeliveryDateDisplayService.BuildDisplayText(order, []);

        Assert.Equal(string.Empty, displayText);
    }

    [Fact]
    public void BuildDisplayText_TreatsMinValueDeliveryDateAsMissing()
    {
        var order = new Order
        {
            Id = "A-1",
            DeliveryDate = DateOnly.MinValue,
            AssignedTourId = string.Empty
        };

        var displayText = OrderDeliveryDateDisplayService.BuildDisplayText(order, []);

        Assert.Equal(string.Empty, displayText);
    }

    [Fact]
    public void BuildDisplayText_UsesExplicitDeliveryDateWithoutPlanningSuffix()
    {
        var order = new Order
        {
            Id = "A-1",
            DeliveryDate = new DateOnly(2026, 6, 15),
            AssignedTourId = "10",
            AvisoStatus = "Best\u00E4tigt"
        };

        var displayText = OrderDeliveryDateDisplayService.BuildDisplayText(
            order,
            [new TourRecord { Id = 10, Date = "20.06.2026" }]);

        Assert.Equal("15.06.2026", displayText);
    }

    [Fact]
    public void BuildDisplayText_UsesAssignedTourDateAsProvisionalWhenDeliveryDateIsMissing()
    {
        var order = new Order
        {
            Id = "A-1",
            DeliveryDate = null,
            AssignedTourId = "10",
            AvisoStatus = "nicht avisiert"
        };

        var displayText = OrderDeliveryDateDisplayService.BuildDisplayText(
            order,
            [new TourRecord { Id = 10, Date = "20.06.2026" }]);

        Assert.Equal("20.06.2026 \u00B7 Provisorisch", displayText);
    }

    [Fact]
    public void BuildDisplayText_UsesAssignedTourDateAsConfirmedWhenAvisoIsConfirmed()
    {
        var order = new Order
        {
            Id = "A-1",
            DeliveryDate = null,
            AssignedTourId = "10",
            AvisoStatus = "Best\u00E4tigt"
        };

        var displayText = OrderDeliveryDateDisplayService.BuildDisplayText(
            order,
            [new TourRecord { Id = 10, Date = "2026-06-20" }]);

        Assert.Equal("20.06.2026 \u00B7 Best\u00E4tigt", displayText);
    }
}
