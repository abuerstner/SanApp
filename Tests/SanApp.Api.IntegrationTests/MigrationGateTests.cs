using Npgsql;
using SanApp.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace SanApp.Api.IntegrationTests;

public sealed class MigrationGateTests
{
    [Fact]
    public async Task FailedMigrationStaysPendingUntilExplicitRecovery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_gate_recovery")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await database.StartAsync(cancellationToken);

        var gate = new MigrationGate(database.GetConnectionString());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.ApplyMigrationsAsync(_ => throw new InvalidOperationException("migration failed"), cancellationToken));
        var migrationRuns = 0;
        gate = new MigrationGate(database.GetConnectionString());
        var pendingError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.ApplyMigrationsAsync(_ =>
            {
                migrationRuns++;
                return Task.CompletedTask;
            }, cancellationToken));
        Assert.Contains("ausstehend", pendingError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, migrationRuns);

        await gate.AuthorizeRecoveryAsync(cancellationToken);
        await gate.ApplyMigrationsAsync(_ =>
        {
            migrationRuns++;
            return Task.CompletedTask;
        }, cancellationToken);

        Assert.Equal(1, migrationRuns);
        await gate.EnsureReadyAsync(cancellationToken);
    }

    [Fact]
    public async Task UnavailableGateDatabasePreventsMigration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 1,
            Database = "unavailable",
            Username = "unavailable",
            Password = "unavailable",
            Timeout = 1,
            CommandTimeout = 1
        }.ConnectionString;
        var migrationStarted = false;
        var gate = new MigrationGate(connectionString);

        await Assert.ThrowsAsync<NpgsqlException>(
            () => gate.ApplyMigrationsAsync(_ =>
            {
                migrationStarted = true;
                return Task.CompletedTask;
            }, cancellationToken));

        Assert.False(migrationStarted);
    }

    [Fact]
    public async Task ConcurrentMigrationAttemptsAreSerializedAcrossGateConnections()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_gate_concurrency")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await database.StartAsync(cancellationToken);

        var gate = new MigrationGate(database.GetConnectionString());
        var firstMigrationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstMigration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeMigrations = 0;
        var maximumConcurrentMigrations = 0;
        var migrationCalls = 0;

        async Task Apply(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref activeMigrations);
            UpdateMaximum(ref maximumConcurrentMigrations, active);
            var call = Interlocked.Increment(ref migrationCalls);
            if (call == 1)
            {
                firstMigrationStarted.SetResult();
                await releaseFirstMigration.Task.WaitAsync(cancellationToken);
            }

            Interlocked.Decrement(ref activeMigrations);
        }

        var first = gate.ApplyMigrationsAsync(Apply, cancellationToken);
        await firstMigrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        var second = gate.ApplyMigrationsAsync(Apply, cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        releaseFirstMigration.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, migrationCalls);
        Assert.Equal(1, maximumConcurrentMigrations);
    }

    [Fact]
    public async Task InterruptedMigrationRemainsPendingUntilOperatorRecovery()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_gate_interrupted")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await database.StartAsync(testCancellationToken);

        using var cancellation = new CancellationTokenSource();
        var gate = new MigrationGate(database.GetConnectionString());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.ApplyMigrationsAsync(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        }, cancellation.Token));

        gate = new MigrationGate(database.GetConnectionString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.EnsureReadyAsync(testCancellationToken));
        await gate.AuthorizeRecoveryAsync(testCancellationToken);
        await gate.EnsureReadyAsync(testCancellationToken);
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref maximum);
            if (current >= value)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maximum, value, current) != current);
    }
}
