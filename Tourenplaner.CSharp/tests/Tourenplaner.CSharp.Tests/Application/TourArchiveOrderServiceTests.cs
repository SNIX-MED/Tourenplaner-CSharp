using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourArchiveOrderServiceTests
{
    private readonly TourArchiveOrderService _service = new();

    [Fact]
    public void Apply_Yes_MarksOnlyReferencedPartialDeliveryAsDelivered()
    {
        var order = CreatePartialOrder();
        order.Deliveries[0].AssignedTourId = "17";
        order.Deliveries[0].Status = OrderDeliveryStatus.Planned;
        var tour = CreateTour(17, order.Id, order.Deliveries[0].Id);
        var completedAt = new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.FromHours(2));

        var result = _service.Apply(tour, [order], true, completedAt);

        Assert.Equal(1, result.ArchivedPartialDeliveryCount);
        Assert.Equal(OrderDeliveryStatus.Delivered, order.Deliveries[0].Status);
        Assert.Equal(completedAt, order.Deliveries[0].DeliveredAt);
        Assert.Null(order.Deliveries[0].AssignedTourId);
        Assert.Equal(OrderDeliveryStatus.ReadyForPlanning, order.Deliveries[1].Status);
        Assert.False(order.IsArchived);
    }

    [Fact]
    public void Apply_No_ReleasesOnlyReferencedPartialDeliveryForPlanning()
    {
        var order = CreatePartialOrder();
        order.Deliveries[0].AssignedTourId = "17";
        order.Deliveries[0].Status = OrderDeliveryStatus.Planned;
        order.Deliveries[1].AssignedTourId = "18";
        order.Deliveries[1].Status = OrderDeliveryStatus.Planned;
        var tour = CreateTour(17, order.Id, order.Deliveries[0].Id);

        var result = _service.Apply(tour, [order], false, DateTimeOffset.Now);

        Assert.Equal(1, result.ReleasedPartialDeliveryCount);
        Assert.Null(order.Deliveries[0].AssignedTourId);
        Assert.Equal(OrderDeliveryStatus.ReadyForPlanning, order.Deliveries[0].Status);
        Assert.Equal("18", order.Deliveries[1].AssignedTourId);
        Assert.Equal(OrderDeliveryStatus.Planned, order.Deliveries[1].Status);
        Assert.Equal("18", order.AssignedTourId);
    }

    [Fact]
    public void Apply_Yes_ArchivesOrderAfterItsSecondPartialDelivery()
    {
        var order = CreatePartialOrder();
        order.Deliveries[0].Status = OrderDeliveryStatus.Delivered;
        order.Deliveries[0].DeliveredAt = DateTimeOffset.Now.AddDays(-1);
        order.Deliveries[1].AssignedTourId = "17";
        order.Deliveries[1].Status = OrderDeliveryStatus.Planned;
        var tour = CreateTour(17, order.Id, order.Deliveries[1].Id);

        _service.Apply(tour, [order], true, DateTimeOffset.Now);

        Assert.Equal(OrderDeliveryStatus.Delivered, order.Deliveries[1].Status);
        Assert.True(order.IsArchived);
    }

    [Fact]
    public void GetImpact_UsesStopsEvenWhenAggregateOrderAssignmentIsEmpty()
    {
        var order = CreatePartialOrder();
        order.AssignedTourId = string.Empty;
        order.Deliveries[0].AssignedTourId = "17";
        order.Deliveries[0].Status = OrderDeliveryStatus.Planned;
        var tour = CreateTour(17, order.Id, order.Deliveries[0].Id);

        var impact = _service.GetImpact(tour, [order]);

        Assert.Equal(0, impact.WholeOrderCount);
        Assert.Equal(1, impact.PartialDeliveryCount);
    }

    [Fact]
    public void Apply_Yes_PreservesExistingWholeOrderArchiveBehavior()
    {
        var order = new Order { Id = "A-2", AssignedTourId = "17" };
        var tour = CreateTour(17, order.Id, string.Empty);

        var result = _service.Apply(tour, [order], true, DateTimeOffset.Now);

        Assert.Equal(1, result.ArchivedOrderCount);
        Assert.True(order.IsArchived);
        Assert.Equal("17", order.AssignedTourId);
    }

    private static Order CreatePartialOrder() => new()
    {
        Id = "A-1",
        Deliveries =
        [
            new OrderDelivery { Id = "D-1", SequenceNumber = 1, Status = OrderDeliveryStatus.ReadyForPlanning },
            new OrderDelivery { Id = "D-2", SequenceNumber = 2, Status = OrderDeliveryStatus.ReadyForPlanning }
        ]
    };

    private static TourRecord CreateTour(int id, string orderId, string deliveryId) => new()
    {
        Id = id,
        Stops =
        [
            new TourStopRecord
            {
                Id = "S-1",
                Auftragsnummer = orderId,
                DeliveryId = deliveryId
            }
        ]
    };
}
