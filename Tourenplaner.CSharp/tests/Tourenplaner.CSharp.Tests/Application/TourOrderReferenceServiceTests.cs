using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class TourOrderReferenceServiceTests
{
    [Fact]
    public void ReconciliationKeepsDeliveryAddressInsteadOfReplacingItWithBillingAddress()
    {
        var order = new Order { Id = "221878", CustomerName = "Familie Wiesner", Address = "Zürichstrasse 131, 8600 Dübendorf",
            DeliveryAddress = new DeliveryAddressInfo { Street = "Hauptstrasse", HouseNumber = "41", PostalCode = "4153", City = "Reinach BL" },
            Location = new GeoPoint(47.4921842,7.5896901) };
        var stop = new TourStopRecord { Id = "auftrag:221878", Auftragsnummer = order.Id, Address = order.Address };
        var tours = new List<TourRecord> { new() { Id = 20, Stops = [stop] } };
        TourOrderReferenceService.ReconcileActiveToursWithOrders(tours,[order]);
        Assert.Equal("Hauptstrasse 41, 4153 Reinach BL",stop.Address);
        Assert.Equal(order.Location.Latitude,stop.Lat);
        TourOrderReferenceService.ReconcileActiveToursWithOrders(tours,[order]);
        Assert.Equal("Hauptstrasse 41, 4153 Reinach BL",stop.Address);
        Assert.Equal("Zürichstrasse 131, 8600 Dübendorf",order.Address);
    }

    [Fact]
    public void RemoveStopsWithoutOrders_RemovesOnlyMissingOrderStopsFromActiveTours()
    {
        var activeTour = new TourRecord
        {
            Id = 10,
            Stops =
            [
                new TourStopRecord
                {
                    Id = TourStopIdentity.CompanyStartStopId,
                    Auftragsnummer = TourStopIdentity.CompanyStartOrderNumber
                },
                new TourStopRecord { Id = "stop-existing", Auftragsnummer = "A-100" },
                new TourStopRecord { Id = "stop-missing", Auftragsnummer = "A-200" },
                new TourStopRecord { Id = "pause:1", StopKind = "pause", Auftragsnummer = "pause:1" },
                new TourStopRecord
                {
                    Id = TourStopIdentity.CompanyEndStopId,
                    Auftragsnummer = TourStopIdentity.CompanyEndOrderNumber
                }
            ]
        };
        var archivedTour = new TourRecord
        {
            Id = 20,
            IsArchived = true,
            Stops =
            [
                new TourStopRecord { Id = "archived-missing", Auftragsnummer = "A-200" }
            ]
        };

        var result = TourOrderReferenceService.RemoveStopsWithoutOrders(
            [activeTour, archivedTour],
            ["A-100"]);

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.RemovedStopCount);
        Assert.Equal([10], result.ChangedTourIds);
        Assert.DoesNotContain(activeTour.Stops, x => x.Auftragsnummer == "A-200");
        Assert.Contains(activeTour.Stops, x => x.Auftragsnummer == "A-100");
        Assert.Contains(activeTour.Stops, x => x.StopKind == "pause");
        Assert.Contains(activeTour.Stops, TourStopIdentity.IsCompanyStop);
        Assert.Contains(archivedTour.Stops, x => x.Auftragsnummer == "A-200");
    }

    [Fact]
    public void ReconcileActiveToursWithOrders_RemovesTourWhenNoOrderStopsRemain()
    {
        var tours = new List<TourRecord>
        {
            new()
            {
                Id = 10,
                Stops =
                [
                    new TourStopRecord
                    {
                        Id = TourStopIdentity.CompanyStartStopId,
                        Auftragsnummer = TourStopIdentity.CompanyStartOrderNumber
                    },
                    new TourStopRecord { Id = "stop-missing", Auftragsnummer = "A-200" },
                    new TourStopRecord { Id = "pause:1", StopKind = "pause", Auftragsnummer = "pause:1" },
                    new TourStopRecord
                    {
                        Id = TourStopIdentity.CompanyEndStopId,
                        Auftragsnummer = TourStopIdentity.CompanyEndOrderNumber
                    }
                ]
            },
            new()
            {
                Id = 20,
                Stops =
                [
                    new TourStopRecord { Id = "stop-existing", Auftragsnummer = "A-100" }
                ]
            }
        };

        var result = TourOrderReferenceService.ReconcileActiveToursWithOrders(
            tours,
            [new Order { Id = "A-100" }]);

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.RemovedStopCount);
        Assert.Equal([10], result.DeletedTourIds);
        Assert.Empty(result.ArchivedTourIds);
        Assert.DoesNotContain(tours, x => x.Id == 10);
        Assert.Contains(tours, x => x.Id == 20);
    }

    [Fact]
    public void ReconcileActiveToursWithOrders_RemovesNonMapOrderStops()
    {
        var tours = new List<TourRecord>
        {
            new()
            {
                Id = 10,
                Stops =
                [
                    new TourStopRecord { Id = "stop-map", Auftragsnummer = "A-100" },
                    new TourStopRecord { Id = "stop-non-map", Auftragsnummer = "A-200" }
                ]
            }
        };

        var result = TourOrderReferenceService.ReconcileActiveToursWithOrders(
            tours,
            [
                new Order { Id = "A-100", Type = OrderType.Map },
                new Order { Id = "A-200", Type = OrderType.NonMap }
            ]);

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.RemovedStopCount);
        Assert.Equal([10], result.RescheduledTourIds);
        Assert.Contains(tours.Single().Stops, x => x.Auftragsnummer == "A-100");
        Assert.DoesNotContain(tours.Single().Stops, x => x.Auftragsnummer == "A-200");
    }

    [Fact]
    public void ReconcileActiveToursWithOrders_ArchivesTourWhenAllOrderStopsAreArchived()
    {
        var tours = new List<TourRecord>
        {
            new()
            {
                Id = 10,
                Stops =
                [
                    new TourStopRecord { Id = "stop-a", Auftragsnummer = "A-100" },
                    new TourStopRecord { Id = "stop-b", Auftragsnummer = "A-200" }
                ]
            },
            new()
            {
                Id = 20,
                Stops =
                [
                    new TourStopRecord { Id = "stop-c", Auftragsnummer = "A-300" },
                    new TourStopRecord { Id = "stop-d", Auftragsnummer = "A-400" }
                ]
            }
        };

        var result = TourOrderReferenceService.ReconcileActiveToursWithOrders(
            tours,
            [
                new Order { Id = "A-100", IsArchived = true },
                new Order { Id = "A-200", IsArchived = true },
                new Order { Id = "A-300", IsArchived = true },
                new Order { Id = "A-400", IsArchived = false }
            ]);

        Assert.True(result.HasChanges);
        Assert.Equal([10], result.ArchivedTourIds);
        Assert.Empty(result.DeletedTourIds);
        Assert.True(tours.Single(x => x.Id == 10).IsArchived);
        Assert.False(tours.Single(x => x.Id == 20).IsArchived);
    }

    [Fact]
    public void ReconcileActiveToursWithOrders_UpdatesChangedPinAndAddressAndReschedulesTour()
    {
        var stop = new TourStopRecord
        {
            Id = "stop-a",
            Auftragsnummer = "A-100",
            Name = "Alter Name",
            Address = "Alte Adresse",
            Lat = 47.0,
            Lng = 8.0
        };
        var tours = new List<TourRecord>
        {
            new() { Id = 10, Stops = [stop] }
        };
        var order = new Order
        {
            Id = "A-100",
            Type = OrderType.Map,
            CustomerName = "Neuer Name",
            Address = "Neue Strasse 10, 8000 Zürich",
            Location = new GeoPoint(47.4, 8.5)
        };

        var result = TourOrderReferenceService.ReconcileActiveToursWithOrders(tours, [order]);

        Assert.True(result.HasChanges);
        Assert.Equal([10], result.RescheduledTourIds);
        Assert.Equal(order.CustomerName, stop.Name);
        Assert.Equal(order.Address, stop.Address);
        Assert.Equal(order.Location.Latitude, stop.Lat);
        Assert.Equal(order.Location.Longitude, stop.Lng);
        Assert.Equal(order.Location.Longitude, stop.Lon);
    }

    [Fact]
    public void ReconcileActiveToursWithOrders_RemovesOnlyDeliveredPartialDeliveryStop()
    {
        var tours = new List<TourRecord>
        {
            new()
            {
                Id = 10,
                Stops =
                [
                    new TourStopRecord { Id = "stop-d1", Auftragsnummer = "A-100", DeliveryId = "D1" },
                    new TourStopRecord { Id = "stop-d2", Auftragsnummer = "A-100", DeliveryId = "D2" }
                ]
            }
        };
        var order = new Order
        {
            Id = "A-100",
            Deliveries =
            [
                new OrderDelivery { Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.Delivered },
                new OrderDelivery { Id = "D2", SequenceNumber = 2, Status = OrderDeliveryStatus.Planned, AssignedTourId = "10" }
            ]
        };

        var result = TourOrderReferenceService.ReconcileActiveToursWithOrders(tours, [order]);

        Assert.Equal(1, result.RemovedStopCount);
        Assert.Equal([10], result.RescheduledTourIds);
        Assert.DoesNotContain(tours.Single().Stops, x => x.DeliveryId == "D1");
        Assert.Contains(tours.Single().Stops, x => x.DeliveryId == "D2");
    }

    [Fact]
    public void ReconcileActiveToursWithOrders_RemovesObsoleteDeliveryStopAfterMerge()
    {
        var tours = new List<TourRecord>
        {
            new()
            {
                Id = 10,
                Stops =
                [
                    new TourStopRecord { Id = "stop-retained", Auftragsnummer = "A-100", DeliveryId = "D1" },
                    new TourStopRecord { Id = "stop-obsolete", Auftragsnummer = "A-100", DeliveryId = "D2" }
                ]
            }
        };
        var order = new Order
        {
            Id = "A-100",
            Deliveries =
            [
                new OrderDelivery { Id = "D1", SequenceNumber = 1, Status = OrderDeliveryStatus.Planned, AssignedTourId = "10" }
            ]
        };

        var result = TourOrderReferenceService.ReconcileActiveToursWithOrders(tours, [order]);

        Assert.Equal(1, result.RemovedStopCount);
        Assert.Equal([10], result.RescheduledTourIds);
        Assert.Contains(tours.Single().Stops, x => x.DeliveryId == "D1");
        Assert.DoesNotContain(tours.Single().Stops, x => x.DeliveryId == "D2");
    }
}
