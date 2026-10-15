using System.Text;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

[Collection("PostgreSql client context")]
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
        Assert.True(Encoding.UTF8.GetByteCount(result) <= 63);
        Assert.Equal(PostgreSqlClientContext.LockSessionId, result.Split('|')[3]);
    }

    [Theory]
    [InlineData("12345678901234")]
    [InlineData("123456789012345")]
    [InlineData("Ein sehr langer Benutzername")]
    [InlineData("Müller Verkauf Büro")]
    [InlineData("ÄÖÜäöüßÄÖÜäöüß")]
    [InlineData("😀😀😀😀😀😀😀😀")]
    [InlineData("Name|mit|Trennzeichen")]
    public void ApplicationName_PreservesFullLockSessionWithinPostgreSqlByteLimit(string userName)
    {
        var client = Guid.Parse("d6ff7622-f620-48dd-b384-7241691ebc38");
        PostgreSqlClientContext.Configure(client, userName);
        var result = PostgreSqlClientContext.BuildApplicationName();
        var bytes = Encoding.UTF8.GetBytes(result);

        // Reproduce the server's maximum application_name size. The old code
        // loses part of the session here and treats its own locks as foreign.
        var serverValue = Encoding.UTF8.GetString(bytes.Take(63).ToArray());
        Assert.Equal(PostgreSqlClientContext.LockSessionId, serverValue.Split('|')[3]);
        Assert.Equal(4, result.Split('|').Length);
        Assert.Equal(client.ToString("N"), result.Split('|')[1]);
        Assert.True(bytes.Length <= 63);
        Assert.DoesNotContain('\uFFFD', result);
    }
}
