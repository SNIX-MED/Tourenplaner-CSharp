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
    public void Validate_IncludesCompanyEndStopWithoutOrderNumber()
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
    public void Validate_IgnoresCanonicalCompanyStartAndIncludesCanonicalCompanyEnd()
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
}
