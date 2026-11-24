using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace SanApp.Infrastructure.Persistence;

internal static class IdentityWriteLock
{
    private const long LockId = 0x53414E415050;

    public static async Task AcquireAsync(DatabaseFacade database, CancellationToken cancellationToken)
    {
        var connection = database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(@lockId)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lockId";
        parameter.Value = LockId;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
