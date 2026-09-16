using System.Reflection;
using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class WebfleetConnectServiceTests
{
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

    private static IReadOnlyList<WebfleetVehicleSnapshot> InvokeParseVehicles(string response)
    {
        var method = typeof(WebfleetConnectService).GetMethod("ParseVehicles", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IReadOnlyList<WebfleetVehicleSnapshot>>(method.Invoke(null, [response]));
    }
}
