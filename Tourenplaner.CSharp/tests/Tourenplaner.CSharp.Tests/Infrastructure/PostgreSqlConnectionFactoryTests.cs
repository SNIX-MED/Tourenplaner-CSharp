using Npgsql;
using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public sealed class PostgreSqlConnectionFactoryTests
{
    [Fact]
    public void QueryPoolIsSmallAndReleasesIdleConnections()
    {
        using var connection = new PostgreSqlConnectionFactory().CreateConnection(new PostgreSqlStorageSettings());
        var options = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        Assert.True(options.Pooling);
        Assert.Equal(6, options.MaxPoolSize);
        Assert.Equal(0, options.MinPoolSize);
        Assert.Equal(30, options.ConnectionIdleLifetime);
        Assert.Equal(10, options.ConnectionPruningInterval);
    }

    [Fact]
    public void ListenerDoesNotOccupyQueryPoolAndKeepsClientIdentity()
    {
        var factory = new PostgreSqlConnectionFactory();
        using var query = factory.CreateConnection(new PostgreSqlStorageSettings());
        using var listener = factory.CreateConnection(new PostgreSqlStorageSettings(), dedicatedListener: true);
        var options = new NpgsqlConnectionStringBuilder(listener.ConnectionString);
        Assert.False(options.Pooling);
        Assert.Equal(new NpgsqlConnectionStringBuilder(query.ConnectionString).ApplicationName, options.ApplicationName);
    }

    [Fact]
    public async Task TemporaryCapacityExhaustionRetriesOnlyConnectionOpening()
    {
        var calls = 0;
        await PostgreSqlConnectionFactory.OpenWithCapacityRetryAsync(_ => ++calls == 1
            ? Task.FromException(new PostgresException("full", "FATAL", "FATAL", "53300"))
            : Task.CompletedTask);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PermanentExhaustionStopsAfterThreeAttempts()
    {
        var calls = 0;
        var error = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlConnectionFactory.OpenWithCapacityRetryAsync(_ =>
        {
            calls++;
            return Task.FromException(new PostgresException("full", "FATAL", "FATAL", "53300"));
        }));
        Assert.Equal(3, calls);
        Assert.Equal("53300", error.SqlState);
    }

    [Fact]
    public async Task AuthenticationFailureIsNotRetried()
    {
        var calls = 0;
        await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlConnectionFactory.OpenWithCapacityRetryAsync(_ =>
        {
            calls++;
            return Task.FromException(new PostgresException("denied", "FATAL", "FATAL", "28P01"));
        }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationDuringRetryStopsFurtherConnections()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PostgreSqlConnectionFactory.OpenWithCapacityRetryAsync(_ =>
        {
            calls++;
            cancellation.Cancel();
            return Task.FromException(new PostgresException("full", "FATAL", "FATAL", "53300"));
        }, cancellation.Token));
        Assert.Equal(1, calls);
    }
}
