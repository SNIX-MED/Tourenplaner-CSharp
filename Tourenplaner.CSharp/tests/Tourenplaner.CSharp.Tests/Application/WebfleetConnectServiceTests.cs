using System.Reflection;
using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class WebfleetConnectServiceTests
{
    [Theory]
    [InlineData("sendDestinationOrderExtern", 2, true)]
    [InlineData("updateDestinationOrderExtern", 1, true)]
    [InlineData("sendDestinationOrderExtern", -7, true)]
    [InlineData("sendDestinationOrderExtern", 2, false)]
    [InlineData("updateDestinationOrderExtern", 2, false)]
    public void DestinationOrderQuery_PreservesTourDayWithoutTimezoneOrArrivalTime(string action, int offsetHours, bool hasArrival)
    {
        var order = new WebfleetDestinationOrderRequest("vehicle", "T4-3-221871", "Tour", 47, 9,
            "CH", "", "", "Street", new DateOnly(2026, 9, 22),
            hasArrival ? new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.FromHours(offsetHours)) : null,
            hasArrival ? 60 : null);
        var method = typeof(WebfleetConnectService).GetMethod("BuildDestinationOrderQuery", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var query = Assert.IsType<Dictionary<string, string?>>(method.Invoke(null,
            [new Tourenplaner.CSharp.Domain.Models.WebfleetConnectionSettings(), order, action]));

        Assert.Equal(action, query["action"]);
        Assert.Equal("true", query["useISO8601"]);
        Assert.Equal("2026-09-22", query["orderdate"]);
        Assert.Equal(hasArrival ? "09:00:00" : null, query["ordertime"]);
        Assert.Equal(hasArrival ? "60" : null, query["arrivaltolerance"]);
    }

    [Fact]
    public void ParseVehicles_ParsesSingleHeaderlessObjectReportRow()
    {
        const string response = "001;uid-123;Testfahrzeug;Janine Fäsi;47581820;9066929;2026-09-15T10:15:00Z;Märstetten;;;";

        var vehicles = InvokeParseVehicles(response);

        var vehicle = Assert.Single(vehicles);
        Assert.Equal("001", vehicle.ObjectNumber);
        Assert.Equal("uid-123", vehicle.ObjectUid);
        Assert.Equal("Testfahrzeug", vehicle.Name);
        Assert.Equal(47.58182, vehicle.Latitude!.Value, 5);
        Assert.Equal(9.066929, vehicle.Longitude!.Value, 5);
    }

    [Fact]
    public void IsWebfleetErrorResponse_RecognizesPlainTextErrorInsteadOfVehicleData()
    {
        var method = typeof(WebfleetConnectService).GetMethod("IsWebfleetErrorResponse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var isError = Assert.IsType<bool>(method.Invoke(null, ["1180,URL credentials are not supported. Use BasicAuth instead."]));

        Assert.True(isError);
    }

    [Fact]
    public void IsWebfleetNoDataResponse_RecognizesCode63AsEmptyResult()
    {
        var method = typeof(WebfleetConnectService).GetMethod("IsWebfleetNoDataResponse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var isNoData = Assert.IsType<bool>(method.Invoke(null, ["63,document contains no data"]));

        Assert.True(isNoData);
    }

    [Fact]
    public void ParseTrackPoints_ParsesCoordinatesAndSortsByPositionTime()
    {
        const string response = "pos_time;latitude;longitude;speed;course\n2026-09-16T09:05:00Z;47581820;9066929;42;120\n2026-09-16T08:55:00Z;47581000;9066000;0;0";

        var method = typeof(WebfleetConnectService).GetMethod("ParseTrackPoints", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var points = Assert.IsAssignableFrom<IReadOnlyList<WebfleetTrackPoint>>(method.Invoke(null, [response]));

        Assert.Equal(2, points.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 8, 55, 0, TimeSpan.Zero), points[0].PositionTime);
        Assert.Equal(47.58182, points[1].Latitude, 5);
        Assert.Equal(9.066929, points[1].Longitude, 5);
        Assert.Equal(42, points[1].SpeedKmh);
    }

    [Fact]
    public void ParseDrivers_ParsesWebfleetDriverIdentity()
    {
        const string response = "driverno;driveruid;name1;name2;name3\n007;driver-uid-7;Pascal;Zander;";
        var method = typeof(WebfleetConnectService).GetMethod("ParseDrivers", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var drivers = Assert.IsAssignableFrom<IReadOnlyList<WebfleetDriverSnapshot>>(method.Invoke(null, [response]));

        var driver = Assert.Single(drivers);
        Assert.Equal("007", driver.DriverNumber);
        Assert.Equal("driver-uid-7", driver.DriverUid);
        Assert.Equal("Pascal Zander", driver.Name);
    }

    [Fact]
    public void ParseWorkingTimes_ParsesPauseWithWorkAppCoordinates()
    {
        const string response = "start_time;end_time;start_latitude;start_longitude;workstate\n2026-09-17T10:30:00Z;2026-09-17T11:00:00Z;47581820;9066929;2";
        var method = typeof(WebfleetConnectService).GetMethod("ParseWorkingTimes", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var intervals = Assert.IsAssignableFrom<IReadOnlyList<WebfleetWorkingTimeInterval>>(method.Invoke(null, [response]));

        var interval = Assert.Single(intervals);
        Assert.Equal(2, interval.WorkState);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 10, 30, 0, TimeSpan.Zero), interval.StartTime);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 11, 0, 0, TimeSpan.Zero), interval.EndTime);
        Assert.Equal(47.58182, interval.Latitude);
        Assert.Equal(9.066929, interval.Longitude);
    }

    [Fact]
    public void ParseOrderIds_ReturnsOnlyOrderIdColumn()
    {
        const string response = "orderdate;orderid;objectuid;orderstate\n2026-09-16;T00001-002-221949;object-uid;100\n2026-09-16;T00001-003-ENDE;object-uid;100";
        var method = typeof(WebfleetConnectService).GetMethod("ParseOrderIds", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var orderIds = Assert.IsAssignableFrom<IReadOnlyList<string>>(method.Invoke(null, [response]));

        Assert.Equal(["T00001-002-221949", "T00001-003-ENDE"], orderIds);
    }

    [Fact]
    public void ParseOrders_ReturnsDetailsUsedForSynchronization()
    {
        const string response = "orderid;ordertext;latitude;longitude;street\nT00001-002-221949;Tour A · Stopp 2: Kunde;47581820;9066929;Weinfelderstrasse 19";
        var method = typeof(WebfleetConnectService).GetMethod("ParseOrders", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var orders = Assert.IsAssignableFrom<IReadOnlyList<WebfleetOrderSnapshot>>(method.Invoke(null, [response]));

        var order = Assert.Single(orders);
        Assert.Equal("T00001-002-221949", order.OrderId);
        Assert.Equal(47.58182, order.Latitude);
        Assert.Equal(9.066929, order.Longitude);
        Assert.Equal("Weinfelderstrasse 19", order.Street);
    }

    private static IReadOnlyList<WebfleetVehicleSnapshot> InvokeParseVehicles(string response)
    {
        var method = typeof(WebfleetConnectService).GetMethod("ParseVehicles", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IReadOnlyList<WebfleetVehicleSnapshot>>(method.Invoke(null, [response]));
    }
}
