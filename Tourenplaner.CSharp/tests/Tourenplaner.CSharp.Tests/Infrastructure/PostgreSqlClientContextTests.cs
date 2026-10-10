using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public sealed class PostgreSqlClientContextTests
{
    [Fact]
    public void ApplicationName_ContainsStableClientAndSanitizedBoundedUser()
    {
        var client = Guid.Parse("d6ff7622-f620-48dd-b384-7241691ebc38");

        PostgreSqlClientContext.Configure(client, "Name|mit|Trennzeichen und sehr langem Zusatz");
        var result = PostgreSqlClientContext.BuildApplicationName();

        Assert.StartsWith("GAWELA|d6ff7622f62048ddb3847241691ebc38|", result, StringComparison.Ordinal);
        Assert.DoesNotContain("|mit|", result, StringComparison.Ordinal);
        Assert.True(result.Length <= 64);
    }
}
