using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace SanApp.Api.IntegrationTests;

public sealed class DatabaseStartupTests
{
    [Fact]
    public async Task PostgreSqlConnectionAndIsolatedTestMigrationWork()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_migrations")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await database.StartAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<MigrationProbeDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .Options;

        await using var context = new MigrationProbeDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
        context.Records.Add(new MigrationProbeRecord { Id = "ready", Value = "migrated" });
        await context.SaveChangesAsync(cancellationToken);

        var record = await context.Records.SingleAsync(cancellationToken);
        Assert.Equal("migrated", record.Value);
        Assert.Contains(
            "202611230001_Probe_Initial",
            await context.Database.GetAppliedMigrationsAsync(cancellationToken));
    }

    [Fact]
    public async Task UnavailablePostgreSqlConnectionFailsExplicitly()
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
        await using var connection = new NpgsqlConnection(connectionString);

        await Assert.ThrowsAsync<NpgsqlException>(() => connection.OpenAsync(cancellationToken));
    }

    [Fact]
    public async Task FailedGatedMigrationPreservesApplicationDataAndBlocksUntilRecovery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var applicationDatabase = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_preserved_data")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await using var gateDatabase = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_preserved_gate")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await Task.WhenAll(applicationDatabase.StartAsync(cancellationToken), gateDatabase.StartAsync(cancellationToken));

        var options = new DbContextOptionsBuilder<MigrationProbeDbContext>()
            .UseNpgsql(applicationDatabase.GetConnectionString())
            .Options;
        await using (var context = new MigrationProbeDbContext(options))
        {
            await context.Database.MigrateAsync(cancellationToken);
            context.Records.Add(new MigrationProbeRecord { Id = "preserved", Value = "existing" });
            await context.SaveChangesAsync(cancellationToken);
        }

        var gate = new SanApp.Infrastructure.Migrations.MigrationGate(gateDatabase.GetConnectionString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.ApplyMigrationsAsync(
            _ => throw new InvalidOperationException("simulated migration failure"), cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.EnsureReadyAsync(cancellationToken));

        await using (var context = new MigrationProbeDbContext(options))
        {
            Assert.Equal("existing", await context.Records.Where(record => record.Id == "preserved")
                .Select(record => record.Value).SingleAsync(cancellationToken));
        }

        await gate.AuthorizeRecoveryAsync(cancellationToken);
        await gate.ApplyMigrationsAsync(async migrationCancellationToken =>
        {
            await using var context = new MigrationProbeDbContext(options);
            await context.Database.MigrateAsync(migrationCancellationToken);
        }, cancellationToken);
        await gate.EnsureReadyAsync(cancellationToken);
    }

    [Fact]
    public async Task MigrationGateUsesAnIndependentPostgreSqlDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var applicationDatabase = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_application")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await using var gateDatabase = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_gate")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await Task.WhenAll(applicationDatabase.StartAsync(cancellationToken), gateDatabase.StartAsync(cancellationToken));

        var gate = new SanApp.Infrastructure.Migrations.MigrationGate(gateDatabase.GetConnectionString());
        await gate.ApplyMigrationsAsync(async migrationCancellationToken =>
        {
            var options = new DbContextOptionsBuilder<MigrationProbeDbContext>()
                .UseNpgsql(applicationDatabase.GetConnectionString())
                .Options;
            await using var context = new MigrationProbeDbContext(options);
            await context.Database.MigrateAsync(migrationCancellationToken);
        }, cancellationToken);

        await gate.EnsureReadyAsync(cancellationToken);
        var applicationOptions = new DbContextOptionsBuilder<MigrationProbeDbContext>()
            .UseNpgsql(applicationDatabase.GetConnectionString())
            .Options;
        await using var migratedContext = new MigrationProbeDbContext(applicationOptions);
        Assert.Contains(
            "202611230001_Probe_Initial",
            await migratedContext.Database.GetAppliedMigrationsAsync(cancellationToken));
    }
}

public sealed class MigrationProbeDbContext(DbContextOptions<MigrationProbeDbContext> options)
    : DbContext(options)
{
    public DbSet<MigrationProbeRecord> Records => Set<MigrationProbeRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MigrationProbeRecord>(entity =>
        {
            entity.ToTable("migration_probe_records");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).HasColumnName("id");
            entity.Property(record => record.Value).HasColumnName("value").IsRequired();
        });
    }
}

public sealed class MigrationProbeRecord
{
    public required string Id { get; set; }
    public required string Value { get; set; }
}

[DbContext(typeof(MigrationProbeDbContext))]
[Migration("202611230001_Probe_Initial")]
public sealed class ProbeInitialMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "migration_probe_records",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                value = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_migration_probe_records", record => record.id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "migration_probe_records");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "11.0.0");
        modelBuilder.Entity<MigrationProbeRecord>(entity =>
        {
            entity.ToTable("migration_probe_records");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).HasColumnName("id");
            entity.Property(record => record.Value).HasColumnName("value").IsRequired();
        });
    }
}
