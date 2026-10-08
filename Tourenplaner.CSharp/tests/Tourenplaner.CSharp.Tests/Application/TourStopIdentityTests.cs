using Tourenplaner.CSharp.Application.Common;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TourStopIdentityTests
{
    [Theory]
    [InlineData("pause:abc", "")]
    [InlineData("other", "pause")]
    [InlineData("PAUSE:ABC", "PAUSE")]
    public void IsPauseStop_RecognizesPauseKindAndLegacyIdPrefix(string id, string stopKind)
    {
        var stop = new TourStopRecord { Id = id, StopKind = stopKind };

        Assert.True(TourStopIdentity.IsPauseStop(stop));
    }

    [Fact]
    public void IsPauseStop_DoesNotTreatRegularStopAsPause()
    {
        var stop = new TourStopRecord { Id = "order:123", StopKind = string.Empty };

        Assert.False(TourStopIdentity.IsPauseStop(stop));
    }
}
