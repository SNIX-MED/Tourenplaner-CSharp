using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class OrderDeliveryServiceTests
{
    private readonly OrderDeliveryService _service = new();

    [Fact]
    public void Create_SplitsOnePositionAcrossSeveralDeliveries()
    {
        var order = NewOrder(10);
        var positionId = order.Products[0].PositionId;

        var first = _service.Create(order, [new() { OrderProductPositionId = positionId, Quantity = 6 }], null);
        var second = _service.Create(order, [new() { OrderProductPositionId = positionId, Quantity = 4 }], null);

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(2, second.SequenceNumber);
        Assert.Equal(0, _service.GetRemainingQuantities(order)[positionId]);
    }

    [Fact]
    public void Create_RejectsQuantityAboveRemainder()
    {
        var order = NewOrder(5);
        var positionId = order.Products[0].PositionId;
        _service.Create(order, [new() { OrderProductPositionId = positionId, Quantity = 4 }], null);

        Assert.Throws<InvalidOperationException>(() =>
            _service.Create(order, [new() { OrderProductPositionId = positionId, Quantity = 2 }], null));
    }

    [Fact]
    public void Cancel_ReleasesReservedQuantity()
    {
        var order = NewOrder(5);
        var positionId = order.Products[0].PositionId;
        var delivery = _service.Create(order, [new() { OrderProductPositionId = positionId, Quantity = 3 }], null);

        _service.Cancel(order, delivery.Id);

        Assert.Equal(5, _service.GetRemainingQuantities(order)[positionId]);
    }

    [Fact]
    public void MergePartialDeliveries_CreatesOneCompletePlannedDelivery()
    {
        var order = NewOrder(5);
        order.Notes = "Beim Kunden anmelden";
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Lines = [new() { OrderProductPositionId = "P-1", Quantity = 2 }] },
            new OrderDelivery { Id = "D2", SequenceNumber = 2, Status = OrderDeliveryStatus.Planned, AssignedTourId = "42", Notes = "Lieferung 2/2: Beim Kunden anmelden", Lines = [new() { OrderProductPositionId = "P-1", Quantity = 3 }] }
        ];

        var merged = _service.MergePartialDeliveries(order);

        Assert.Same(merged, Assert.Single(order.Deliveries));
        Assert.Equal("D2", merged.Id);
        Assert.Equal(1, merged.SequenceNumber);
        Assert.Equal(5, Assert.Single(merged.Lines).Quantity);
        Assert.Equal("42", merged.AssignedTourId);
        Assert.Equal(OrderDeliveryStatus.Planned, merged.Status);
        Assert.Equal("Beim Kunden anmelden", order.Notes);
        Assert.Empty(merged.Notes);
    }

    [Fact]
    public void MergePartialDeliveries_RejectsDeliveredPart()
    {
        var order = NewOrder(2);
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.Delivered },
            new OrderDelivery { Id = "D2", SequenceNumber = 2 }
        ];

        Assert.False(_service.CanMergePartialDeliveries(order));
        Assert.Throws<InvalidOperationException>(() => _service.MergePartialDeliveries(order));
    }

    [Fact]
    public void CanSplitSingleDelivery_AllowsMergedCompleteDelivery()
    {
        var order = NewOrder(5);
        order.Deliveries =
        [
            new OrderDelivery
            {
                Id = "D1",
                SequenceNumber = 1,
                Status = OrderDeliveryStatus.Planned,
                AssignedTourId = "42",
                Lines = [new() { OrderProductPositionId = "P-1", Quantity = 5 }]
            }
        ];

        Assert.True(_service.CanSplitSingleDelivery(order));
    }

    [Fact]
    public void CanSplitSingleDelivery_RejectsDeliveredDelivery()
    {
        var order = NewOrder(5);
        order.Deliveries =
        [
            new OrderDelivery
            {
                Id = "D1",
                SequenceNumber = 1,
                Status = OrderDeliveryStatus.Delivered,
                Lines = [new() { OrderProductPositionId = "P-1", Quantity = 5 }]
            }
        ];

        Assert.False(_service.CanSplitSingleDelivery(order));
    }

    [Fact]
    public void Normalize_MigratesLegacyTourAssignment()
    {
        var order = NewOrder(3);
        order.AssignedTourId = "42";

        Assert.True(_service.Normalize(order));
        var delivery = Assert.Single(order.Deliveries);
        Assert.Equal("42", delivery.AssignedTourId);
        Assert.Equal(OrderDeliveryStatus.Planned, delivery.Status);
        Assert.Equal(3, Assert.Single(delivery.Lines).Quantity);
    }

    [Fact]
    public void ResolveWeight_UsesDeliveredPartialQuantity()
    {
        var order = NewOrder(10);
        order.Products[0].UnitWeightKg = 2.5;
        var delivery = _service.Create(order, [new() { OrderProductPositionId = order.Products[0].PositionId, Quantity = 4 }], null);

        Assert.Equal(10, _service.ResolveWeightKg(order, delivery));
    }

    [Fact]
    public void ResolveProducts_ReturnsOnlyProductsContainedInDelivery()
    {
        var order = NewOrder(1);
        order.Products.Add(new OrderProductInfo
        {
            PositionId = "P-2",
            Name = "Produkt 2",
            Quantity = 1,
            DeliveryStatus = OrderProductInfo.OrderedStatus
        });
        order.Products[0].DeliveryStatus = OrderProductInfo.InStockStatus;
        var delivery = new OrderDelivery
        {
            Id = "D1",
            Lines = [new OrderDeliveryLine { OrderProductPositionId = "P-1", Quantity = 1 }]
        };

        var products = _service.ResolveProducts(order, delivery);

        Assert.Equal(Order.ReadyToDeliverStatus, Order.ResolveOrderStatusFromProducts(products));
        Assert.DoesNotContain(products, x => x.PositionId == "P-2");
    }

    [Fact]
    public void TryValidate_RejectsReducingOrderBelowAllocatedQuantity()
    {
        var order = NewOrder(5);
        _service.Create(order, [new() { OrderProductPositionId = "P-1", Quantity = 4 }], null);
        order.Products[0].Quantity = 3;

        Assert.False(_service.TryValidate(order, out var error));
        Assert.Contains("bereits 4", error);
    }

    [Fact]
    public void EnsureRemainderDelivery_CreatesSecondPlanableDelivery()
    {
        var order = NewOrder(10);
        _service.Create(order, [new() { OrderProductPositionId = "P-1", Quantity = 6 }], null);

        var remainder = _service.EnsureRemainderDelivery(order);

        Assert.NotNull(remainder);
        Assert.Equal(2, remainder.SequenceNumber);
        Assert.Equal(OrderDeliveryStatus.ReadyForPlanning, remainder.Status);
        Assert.Equal(4, Assert.Single(remainder.Lines).Quantity);
        Assert.Equal(0, _service.GetRemainingQuantities(order)["P-1"]);
    }

    [Fact]
    public void EnsureRemainderDelivery_IsIdempotent()
    {
        var order = NewOrder(10);
        _service.Create(order, [new() { OrderProductPositionId = "P-1", Quantity = 6 }], null);

        _service.EnsureRemainderDelivery(order);
        var secondCall = _service.EnsureRemainderDelivery(order);

        Assert.Null(secondCall);
        Assert.Equal(2, order.Deliveries.Count);
    }

    [Fact]
    public void Create_RejectsThirdDelivery()
    {
        var order = NewOrder(3);
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1 },
            new OrderDelivery { Id = "D2", SequenceNumber = 2 }
        ];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.Create(order, [new() { OrderProductPositionId = "P-1", Quantity = 1 }], null));

        Assert.Contains("maximal zwei", exception.Message);
    }

    [Fact]
    public void TryValidate_RejectsIncompleteDistributionAcrossTwoDeliveries()
    {
        var order = NewOrder(5);
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Lines = [new() { OrderProductPositionId = "P-1", Quantity = 2 }] },
            new OrderDelivery { Id = "D2", SequenceNumber = 2, Lines = [new() { OrderProductPositionId = "P-1", Quantity = 2 }] }
        ];

        Assert.False(_service.TryValidate(order, out var error));
        Assert.Contains("genau 5", error);
    }

    [Fact]
    public void TryValidateUpdate_RejectsQuantityChangeForDeliveredProduct()
    {
        var existing = NewOrder(2);
        existing.Deliveries =
        [
            new OrderDelivery
            {
                Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.Delivered,
                Lines = [new() { OrderProductPositionId = "P-1", Quantity = 1 }]
            },
            new OrderDelivery
            {
                Id = "D2", SequenceNumber = 2,
                Lines = [new() { OrderProductPositionId = "P-1", Quantity = 1 }]
            }
        ];
        var updated = NewOrder(3);
        updated.Deliveries = existing.Deliveries;
        updated.Deliveries[1].Lines[0].Quantity = 2;

        Assert.False(_service.TryValidateUpdate(existing, updated, out var error));
        Assert.Contains("bereits gelieferten", error);
    }

    [Fact]
    public void Normalize_RemovesLegacyInternalRemainderNote()
    {
        var order = NewOrder(1);
        order.Deliveries = [new OrderDelivery
        {
            Id = "D1",
            SequenceNumber = 1,
            Notes = "Automatisch erzeugte Restlieferung",
            Lines = [new OrderDeliveryLine { OrderProductPositionId = "P-1", Quantity = 1 }]
        }];

        Assert.True(_service.Normalize(order));
        Assert.Empty(order.Deliveries[0].Notes);
    }

    [Fact]
    public void DistributeOrderNotesToDeliveries_CopiesAndClearsGeneralNote()
    {
        var order = NewOrder(2);
        order.Notes = "Beim Kunden anmelden";
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1 },
            new OrderDelivery { Id = "D2", SequenceNumber = 2 }
        ];

        Assert.True(_service.DistributeOrderNotesToDeliveries(order));
        Assert.Equal("Lieferung 1/2: Beim Kunden anmelden", order.Deliveries[0].Notes);
        Assert.Equal("Lieferung 2/2: Beim Kunden anmelden", order.Deliveries[1].Notes);
        Assert.Empty(order.Notes);
    }

    [Fact]
    public void DistributeOrderNotesToDeliveries_UpdatesExistingPrefixWithoutDuplicatingIt()
    {
        var order = NewOrder(2);
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Notes = "Lieferung 1/1: Rampe links" },
            new OrderDelivery { Id = "D2", SequenceNumber = 2, Notes = "Empfang anrufen" }
        ];

        Assert.True(_service.DistributeOrderNotesToDeliveries(order));
        Assert.Equal("Lieferung 1/2: Rampe links", order.Deliveries[0].Notes);
        Assert.Equal("Lieferung 2/2: Empfang anrufen", order.Deliveries[1].Notes);
        Assert.DoesNotContain("Lieferung 1/1: Lieferung", order.Deliveries[0].Notes);
    }

    [Fact]
    public void DistributeOrderNotesToDeliveries_SingleInternalDeliveryRestoresRegularOrderNote()
    {
        var order = NewOrder(1);
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Notes = "Lieferung 1/1: Rampe links" }
        ];

        Assert.True(_service.DistributeOrderNotesToDeliveries(order));
        Assert.Equal("Rampe links", order.Notes);
        Assert.Empty(order.Deliveries[0].Notes);
    }

    [Fact]
    public void MarkDelivered_FirstDeliveryKeepsOrderActive_SecondArchivesOrder()
    {
        var order = NewOrder(2);
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.ReadyForPlanning, AssignedTourId = "10" },
            new OrderDelivery { Id = "D2", SequenceNumber = 2, Status = OrderDeliveryStatus.ReadyForPlanning }
        ];

        _service.MarkDelivered(order, "D1", DateTimeOffset.UtcNow);

        Assert.False(order.IsArchived);
        Assert.Equal(OrderDeliveryStatus.Delivered, order.Deliveries[0].Status);
        Assert.Null(order.Deliveries[0].AssignedTourId);

        _service.MarkDelivered(order, "D2", DateTimeOffset.UtcNow);

        Assert.True(order.IsArchived);
        Assert.Equal(OrderDeliveryStatus.Delivered, order.Deliveries[1].Status);
    }

    [Fact]
    public void SynchronizeTourAssignments_RemovesOrphanedBadgeAndKeepsRealAssignment()
    {
        var order = NewOrder(2);
        order.Deliveries =
        [
            new OrderDelivery { Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.Planned, AssignedTourId = "99" },
            new OrderDelivery { Id = "D2", SequenceNumber = 2, Status = OrderDeliveryStatus.ReadyForPlanning }
        ];
        var tours = new List<TourRecord>
        {
            new()
            {
                Id = 42,
                Stops =
                [
                    new TourStopRecord { Auftragsnummer = order.Id, DeliveryId = "D2", DeliverySequenceNumber = 2 }
                ]
            }
        };

        Assert.True(_service.SynchronizeTourAssignments([order], tours));

        Assert.Null(order.Deliveries[0].AssignedTourId);
        Assert.Equal(OrderDeliveryStatus.ReadyForPlanning, order.Deliveries[0].Status);
        Assert.Equal("42", order.Deliveries[1].AssignedTourId);
        Assert.Equal(OrderDeliveryStatus.Planned, order.Deliveries[1].Status);
        Assert.True(string.IsNullOrWhiteSpace(order.AssignedTourId));
    }

    private static Order NewOrder(int quantity) => new()
    {
        Id = "A-1",
        Products = [new OrderProductInfo { PositionId = "P-1", Name = "Produkt", Quantity = quantity }]
    };
}
