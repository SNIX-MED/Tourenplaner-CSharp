using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class TourOrderReferenceServiceTests
{
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
}
