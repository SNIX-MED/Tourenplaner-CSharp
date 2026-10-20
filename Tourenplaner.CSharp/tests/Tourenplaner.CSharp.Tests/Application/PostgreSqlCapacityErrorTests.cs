using Npgsql;
using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class PostgreSqlCapacityErrorTests
{
    [Fact]
    public void CapacityFailureIsRecognizedThroughWrapperButOtherFailuresAreNot()
    {
        Assert.True(PostgreSqlCapacityError.IsCapacityError(new InvalidOperationException("wrapper",
            new PostgresException("full", "FATAL", "FATAL", "53300"))));
        Assert.False(PostgreSqlCapacityError.IsCapacityError(new PostgresException("denied", "FATAL", "FATAL", "28P01")));
    }

    [Fact]
    public void MessageDistinguishesCommitFromReloadFailure()
    {
        Assert.Contains("erfolgreich gespeichert", PostgreSqlCapacityError.SaveMessage(true));
        Assert.Contains("nicht erneut anlegen", PostgreSqlCapacityError.SaveMessage(true));
        Assert.Contains("nicht gespeichert", PostgreSqlCapacityError.SaveMessage(false));
        Assert.Contains("Stopps bleiben erhalten", PostgreSqlCapacityError.SaveMessage(false));
    }
}
