using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SanApp.Application.Identity;
using SanApp.Infrastructure.Identity;
using SanApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SanApp.Api.IntegrationTests;

[Collection("IdentityTestcontainers")]
public sealed class PasswordRecoveryRateLimitTests
{
    [Fact]
    public async Task SharedStoreEnforcesAccountAndIpLimitsAndExpiresEntriesAfterOneHour()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await RateLimitFixture.CreateAsync(cancellationToken);
        var now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);
        var accountIp = System.Net.IPAddress.Parse("192.0.2.10");
        var results = new List<PasswordRecoveryRateLimitDecision>();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            results.Add(await fixture.CreateStore().TryAcquireAsync(
                "USER@EXAMPLE.TEST", accountIp, now, TimeSpan.FromHours(1), 3, 10, cancellationToken));
        }

        Assert.Equal(3, results.Count(result => result == PasswordRecoveryRateLimitDecision.Allowed));
        Assert.Equal(PasswordRecoveryRateLimitDecision.Limited, results[^1]);

        var ipResults = new List<PasswordRecoveryRateLimitDecision>();
        for (var attempt = 0; attempt < 11; attempt++)
        {
            ipResults.Add(await fixture.CreateStore().TryAcquireAsync(
                $"USER{attempt}@EXAMPLE.TEST", System.Net.IPAddress.Parse("192.0.2.20"), now,
                TimeSpan.FromHours(1), 3, 10, cancellationToken));
        }

        Assert.Equal(10, ipResults.Count(result => result == PasswordRecoveryRateLimitDecision.Allowed));
        Assert.Equal(PasswordRecoveryRateLimitDecision.Limited, ipResults[^1]);

        var afterWindow = await fixture.CreateStore().TryAcquireAsync(
            "USER@EXAMPLE.TEST", accountIp, now.AddHours(1), TimeSpan.FromHours(1), 3, 10, cancellationToken);
        Assert.Equal(PasswordRecoveryRateLimitDecision.Allowed, afterWindow);

        var deletedCount = await fixture.CreateStore().DeleteExpiredAsync(now.AddHours(1), cancellationToken);
        Assert.Equal(22, deletedCount);
        Assert.Empty(await fixture.GetStoredKeyHashesAsync(cancellationToken));
    }

    [Fact]
    public async Task StorePersistsOnlyKeyedHashesInsteadOfEmailOrIpValues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await RateLimitFixture.CreateAsync(cancellationToken);

        var result = await fixture.CreateStore().TryAcquireAsync(
            "USER@EXAMPLE.TEST",
            System.Net.IPAddress.Parse("192.0.2.40"),
            new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero),
            TimeSpan.FromHours(1),
            3,
            10,
            cancellationToken);
        var hashes = await fixture.GetStoredKeyHashesAsync(cancellationToken);

        Assert.Equal(PasswordRecoveryRateLimitDecision.Allowed, result);
        Assert.Equal(2, hashes.Count);
        Assert.All(hashes, hash => Assert.Equal(64, hash.Length));
        Assert.DoesNotContain(hashes, hash => hash.Contains("USER@EXAMPLE.TEST", StringComparison.Ordinal));
        Assert.DoesNotContain(hashes, hash => hash.Contains("192.0.2.40", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentScopesShareAtomicAccountLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await RateLimitFixture.CreateAsync(cancellationToken);
        var now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);
        var stores = Enumerable.Range(0, 6).Select(_ => fixture.CreateStore()).ToArray();

        var results = await Task.WhenAll(stores.Select(store => store.TryAcquireAsync(
            "USER@EXAMPLE.TEST",
            System.Net.IPAddress.Parse("192.0.2.30"),
            now,
            TimeSpan.FromHours(1),
            3,
            10,
            cancellationToken)));

        Assert.Equal(3, results.Count(result => result == PasswordRecoveryRateLimitDecision.Allowed));
        Assert.Equal(3, results.Count(result => result == PasswordRecoveryRateLimitDecision.Limited));
    }

    private sealed class RateLimitFixture(PostgreSqlContainer database, string connectionString) : IAsyncDisposable
    {
        private readonly DbContextOptions<SanAppDbContext> options = new DbContextOptionsBuilder<SanAppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        private readonly ConcurrentBag<SanAppDbContext> contexts = [];
        private readonly IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:PasswordRecovery:RateLimitKey"] = Convert.ToBase64String(new byte[32].Select((_, index) => (byte)(index + 1)).ToArray())
            })
            .Build();

        public static async Task<RateLimitFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var database = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("sanapp_password_recovery_limits")
                .WithUsername("sanapp")
                .WithPassword("test-only-password")
                .Build();
            await database.StartAsync(cancellationToken);
            var fixture = new RateLimitFixture(database, database.GetConnectionString());
            await using var context = fixture.CreateContext();
            await context.Database.MigrateAsync(cancellationToken);
            return fixture;
        }

        public PasswordRecoveryRateLimitStore CreateStore()
        {
            var context = CreateContext();
            contexts.Add(context);
            return new PasswordRecoveryRateLimitStore(context, configuration, NullLogger<PasswordRecoveryRateLimitStore>.Instance);
        }

        public async Task<List<string>> GetStoredKeyHashesAsync(CancellationToken cancellationToken)
        {
            await using var context = CreateContext();
            return await context.PasswordRecoveryRateLimitEntries
                .AsNoTracking()
                .Select(entry => entry.KeyHash)
                .ToListAsync(cancellationToken);
        }

        private SanAppDbContext CreateContext() => new(options);

        public async ValueTask DisposeAsync()
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }

            await database.DisposeAsync();
        }
    }
}
