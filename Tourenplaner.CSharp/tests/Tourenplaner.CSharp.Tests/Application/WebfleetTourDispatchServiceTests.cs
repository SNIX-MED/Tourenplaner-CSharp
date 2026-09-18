using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;
using System.Reflection;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class WebfleetTourDispatchServiceTests
{
    [Fact]
    public void Validate_IgnoresLegacyCompanyStartStop()
    {
        var tour = new TourRecord
        {
            Stops =
            [
                new TourStopRecord { Id = "__company_start__", Order = 1 },
                new TourStopRecord { Id = "order-1", Order = 2, Auftragsnummer = "221949", Lat = 47.58, Lon = 9.06 }
            ]
        };
        var vehicle = new Vehicle { WebfleetObjectUid = "object-uid" };

        var errors = new WebfleetTourDispatchService().Validate(tour, vehicle);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_IgnoresCompanyEndStopWithoutOrderNumber()
    {
        var tour = new TourRecord
        {
            Stops =
            [
                new TourStopRecord { Id = "__company_start__", StopKind = "company", Order = 1 },
                new TourStopRecord { Id = "order-1", Order = 2, Auftragsnummer = "221949", Lat = 47.58, Lon = 9.06 },
                new TourStopRecord { Id = "__company_end__", StopKind = "company", Order = 3, Lat = 47.58, Lon = 9.06 }
            ]
        };
        var vehicle = new Vehicle { WebfleetObjectUid = "object-uid" };

        var errors = new WebfleetTourDispatchService().Validate(tour, vehicle);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_IgnoresCanonicalCompanyStartAndEnd()
    {
        var tour = new TourRecord
        {
            Stops =
            [
                new TourStopRecord { Id = TourStopIdentity.CompanyStartStopId, Auftragsnummer = TourStopIdentity.CompanyStartOrderNumber, Order = 1 },
                new TourStopRecord { Id = "order-1", Order = 2, Auftragsnummer = "221949", Lat = 47.58, Lon = 9.06 },
                new TourStopRecord { Id = TourStopIdentity.CompanyEndStopId, Auftragsnummer = TourStopIdentity.CompanyEndOrderNumber, Order = 3, Lat = 47.58, Lon = 9.06 }
            ]
        };
        var vehicle = new Vehicle { WebfleetObjectUid = "object-uid" };

        var errors = new WebfleetTourDispatchService().Validate(tour, vehicle);

        Assert.Empty(errors);
    }

    [Fact]
    public void IsDispatchableStop_ExcludesBothCompanyStops()
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("IsDispatchableStop", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var start = Assert.IsType<bool>(method.Invoke(null, [new TourStopRecord { Id = TourStopIdentity.CompanyStartStopId }]));
        var end = Assert.IsType<bool>(method.Invoke(null, [new TourStopRecord { Id = TourStopIdentity.CompanyEndStopId }]));
        var customer = Assert.IsType<bool>(method.Invoke(null, [new TourStopRecord { Id = "order-1", Auftragsnummer = "221949" }]));

        Assert.False(start);
        Assert.False(end);
        Assert.True(customer);
    }

    [Fact]
    public void BuildWebfleetArrivalWindow_UsesRoundedWindowStartAndTolerance()
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("BuildWebfleetArrivalWindow", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var tour = new TourRecord { Date = "22.09.2026" };
        var stop = new TourStopRecord
        {
            PlannedArrivalOptimistic = "07:42",
            PlannedArrival = "07:42",
            PlannedArrivalPessimistic = "07:42"
        };

        var result = ((DateTimeOffset? PlannedArrival, int? ArrivalToleranceMinutes))method.Invoke(null, [tour, stop])!;

        Assert.Equal(new DateTime(2026, 9, 22, 7, 30, 0), result.PlannedArrival!.Value.DateTime);
        Assert.Equal(15, result.ArrivalToleranceMinutes);
    }

    [Fact]
    public void IsOrderAlreadyPresent_RecognizesWebfleetDuplicateOrderResponse()
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("IsOrderAlreadyPresent", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = Assert.IsType<bool>(method.Invoke(null, [new InvalidOperationException("WEBFLEET-Verbindung fehlgeschlagen: 2515,Order already exists")]));

        Assert.True(result);
    }

    [Fact]
    public void BuildOrderText_UsesDispatchPositionInsteadOfInternalTourPosition()
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("BuildOrderText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var tour = new TourRecord { Name = "Tour 22.09.2026" };
        var stop = new TourStopRecord { Order = 2, Name = "Tunap AG" };

        var text = Assert.IsType<string>(method.Invoke(null, [tour, stop, 1]));

        Assert.Equal("Tour 22.09.2026 · Stopp 1: Tunap AG", text);
    }

    [Fact]
    public void BuildOrderId_UsesUnpaddedTourAndEffectiveStopPosition()
    {
        var method = typeof(WebfleetTourDispatchService).GetMethod("BuildOrderId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var orderId = Assert.IsType<string>(method.Invoke(null, [2, 1, "221770"]));

        Assert.Equal("T2-1-221770", orderId);
    }
}
