using Npgsql;

namespace SanApp.Infrastructure.Migrations;

public sealed class MigrationGate(string connectionString)
{
    private const long AdvisoryLockId = 0x53414E415050;

    public async Task ApplyMigrationsAsync(
        Func<CancellationToken, Task> applyMigrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applyMigrations);

        await using var connection = await OpenLockedConnectionAsync(cancellationToken);
        try
        {
            await EnsureSchemaAsync(connection, cancellationToken);

            if (await IsPendingAsync(connection, cancellationToken))
            {
                throw PendingGateException();
            }

            await SetPendingAsync(connection, true, cancellationToken);
            await applyMigrations(cancellationToken);
            await SetPendingAsync(connection, false, cancellationToken);
        }
        finally
        {
            await ReleaseLockAsync(connection);
        }
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenLockedConnectionAsync(cancellationToken);
        try
        {
            await EnsureSchemaAsync(connection, cancellationToken);

            if (await IsPendingAsync(connection, cancellationToken))
            {
                throw PendingGateException();
            }
        }
        finally
        {
            await ReleaseLockAsync(connection);
        }
    }

    public async Task AuthorizeRecoveryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenLockedConnectionAsync(cancellationToken);
        try
        {
            await EnsureSchemaAsync(connection, cancellationToken);

            if (!await IsPendingAsync(connection, cancellationToken))
            {
                throw new InvalidOperationException("Das Migrations-Gate enthält keinen ausstehenden Zustand zur Freigabe.");
            }

            await SetPendingAsync(connection, false, cancellationToken);
        }
        finally
        {
            await ReleaseLockAsync(connection);
        }
    }

    private async Task<NpgsqlConnection> OpenLockedConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@lockId)", connection)
            {
                CommandTimeout = 0
            };
            command.Parameters.AddWithValue("lockId", AdvisoryLockId);
            await command.ExecuteScalarAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ReleaseLockAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lockId)", connection);
        command.Parameters.AddWithValue("lockId", AdvisoryLockId);
        if (!Equals(await command.ExecuteScalarAsync(), true))
        {
            throw new InvalidOperationException("Die exklusive Migrations-Gate-Sperre konnte nicht freigegeben werden.");
        }
    }

    private static async Task EnsureSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS sanapp_migration_gate (
                id integer PRIMARY KEY CHECK (id = 1),
                is_pending boolean NOT NULL,
                pending_since timestamp with time zone NULL
            );
            INSERT INTO sanapp_migration_gate (id, is_pending, pending_since)
            VALUES (1, FALSE, NULL)
            ON CONFLICT (id) DO NOTHING;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> IsPendingAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT is_pending FROM sanapp_migration_gate WHERE id = 1",
            connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Der Migrations-Gate-Zustand fehlt."));
    }

    private static async Task SetPendingAsync(
        NpgsqlConnection connection,
        bool isPending,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE sanapp_migration_gate SET is_pending = @isPending, pending_since = @pendingSince WHERE id = 1",
            connection);
        command.Parameters.AddWithValue("isPending", isPending);
        command.Parameters.AddWithValue("pendingSince", isPending ? DateTime.UtcNow : DBNull.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Der Migrations-Gate-Zustand konnte nicht aktualisiert werden.");
        }
    }

    private static InvalidOperationException PendingGateException() =>
        new("Das Migrations-Gate ist ausstehend. Start und Migration bleiben bis zur administrativen Prüfung und ausdrücklichen Freigabe gesperrt.");
}
