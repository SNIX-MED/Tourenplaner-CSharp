using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class OrderDeliveryDateDisplayServiceTests
{
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
