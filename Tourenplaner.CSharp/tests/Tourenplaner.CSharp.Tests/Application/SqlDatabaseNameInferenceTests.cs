using Tourenplaner.CSharp.Application.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public class SqlDatabaseNameInferenceTests
{
    [Fact]
    public void InferDatabaseName_ReadsDatabaseFromConnectionString()
    {
        const string connectionString = "Server=localhost;Database=GAWELA-TP;Trusted_Connection=True;";

        var result = SqlDatabaseNameInference.InferDatabaseName(connectionString);

        Assert.Equal("GAWELA_TP", result);
    }

    [Fact]
    public void InferDatabaseName_ReturnsUnknown_WhenMissingDatabasePart()
    {
        const string connectionString = "Server=localhost;Trusted_Connection=True;";

        var result = SqlDatabaseNameInference.InferDatabaseName(connectionString);

        Assert.Equal("unknown_db", result);
    }
}
