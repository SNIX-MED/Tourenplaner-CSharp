using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public class TourFullWarningTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullTourRequiresExplicitDecisionWithoutLegacyReasonOrNote(bool accepted)
    {
        var calls = 0;
        var tour = new TourRecord { Id = 42, Name = "Morgentour", IsFull = true,
            FullReason = "Platz ausgeschöpft", FullNote = "Sperrige Schränke" };
        Assert.Equal(accepted, TourFullWarning.Confirm(tour, message =>
        {
            calls++;
            Assert.Contains("Morgentour", message);
            Assert.DoesNotContain("Platz ausgeschöpft", message);
            Assert.DoesNotContain("Sperrige Schränke", message);
            return accepted;
        }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void UnmarkedTourAndNewDraftDoNotAskForConfirmation()
    {
        Assert.True(TourFullWarning.Confirm(null, _ => throw new Exception("Unexpected warning")));
        Assert.True(TourFullWarning.Confirm(new(), _ => throw new Exception("Unexpected warning")));
        Assert.False(TourFullWarning.Confirm(new() { IsFull = true }, _ => false));
    }
}
