using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class AppSettingsTests
{
    [Fact]
    public void ResolveUserPreference_MigratesLegacyUniformTrafficBufferDefaults()
    {
        var settings = new AppSettings();
        var preference = new UserAppPreference
        {
            TrafficBufferPercentPerThirtyMinutes = 20,
            TrafficBufferPercentFrom0500To0730 = 20,
            TrafficBufferPercentFrom0730To0900 = 20,
            TrafficBufferPercentFrom0900To1530 = 20,
            TrafficBufferPercentFrom1530To1830 = 20
        };

        settings.SetUserPreference("tester", preference);

        var resolved = settings.ResolveUserPreference("tester");

        Assert.Equal(10, resolved.TrafficBufferPercentFrom0500To0730);
        Assert.Equal(20, resolved.TrafficBufferPercentFrom0730To0900);
        Assert.Equal(0, resolved.TrafficBufferPercentFrom0900To1530);
        Assert.Equal(20, resolved.TrafficBufferPercentFrom1530To1830);
    }

    [Fact]
    public void ResolveUserPreference_KeepsCustomTrafficBufferProfile()
    {
        var settings = new AppSettings();
        var preference = new UserAppPreference
        {
            TrafficBufferPercentPerThirtyMinutes = 20,
            TrafficBufferPercentFrom0500To0730 = 15,
            TrafficBufferPercentFrom0730To0900 = 25,
            TrafficBufferPercentFrom0900To1530 = 5,
            TrafficBufferPercentFrom1530To1830 = 30
        };

        settings.SetUserPreference("tester", preference);

        var resolved = settings.ResolveUserPreference("tester");

        Assert.Equal(15, resolved.TrafficBufferPercentFrom0500To0730);
        Assert.Equal(25, resolved.TrafficBufferPercentFrom0730To0900);
        Assert.Equal(5, resolved.TrafficBufferPercentFrom0900To1530);
        Assert.Equal(30, resolved.TrafficBufferPercentFrom1530To1830);
    }

    [Theory]
    [InlineData(DeliveryMethodExtensions.FreiBordsteinkante, 10)]
    [InlineData(DeliveryMethodExtensions.MitVerteilung, 20)]
    [InlineData(DeliveryMethodExtensions.MitVerteilungMontage, 30)]
    public void ResolveMapOrderStayMinutes_UsesConfiguredValuesPerDeliveryType(string deliveryType, int expectedMinutes)
    {
        var settings = new AppSettings
        {
            StayMinutesFreiBordsteinkante = 10,
            StayMinutesMitVerteilung = 20,
            StayMinutesMitVerteilungMontage = 30
        };

        var resolved = settings.ResolveMapOrderStayMinutes(deliveryType);

        Assert.Equal(expectedMinutes, resolved);
    }

    [Fact]
    public void ResolveMapOrderStayMinutes_FallsBackToDefaultsForInvalidValues()
    {
        var settings = new AppSettings
        {
            StayMinutesFreiBordsteinkante = -5,
            StayMinutesMitVerteilung = -1,
            StayMinutesMitVerteilungMontage = -99
        };

        Assert.Equal(AppSettings.DefaultStayMinutesFreiBordsteinkante, settings.ResolveMapOrderStayMinutes(DeliveryMethodExtensions.FreiBordsteinkante));
        Assert.Equal(AppSettings.DefaultStayMinutesMitVerteilung, settings.ResolveMapOrderStayMinutes(DeliveryMethodExtensions.MitVerteilung));
        Assert.Equal(AppSettings.DefaultStayMinutesMitVerteilungMontage, settings.ResolveMapOrderStayMinutes(DeliveryMethodExtensions.MitVerteilungMontage));
    }
}
