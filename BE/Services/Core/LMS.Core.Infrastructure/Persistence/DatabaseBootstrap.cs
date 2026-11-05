using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace LMS.Core.Infrastructure.Persistence;

public static class DatabaseBootstrap
{
    public static async Task MigrateAsync(LmsDbContext db, CancellationToken ct = default)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (await creator.ExistsAsync(ct) && await creator.HasTablesAsync(ct) &&
            !(await db.Database.GetAppliedMigrationsAsync(ct)).Any())
            throw new InvalidOperationException(
                "Existing database has no EF migration history. No migration was applied. " +
                "Stop the API and back up the database. For the exact legacy SQLite schema run " +
                "python scripts/baseline_sqlite.py --database <absolute-path> (verify), then add --apply " +
                "to create a backup and adopt the verified baseline. SQL Server requires a reviewed manual baseline.");
        await db.Database.MigrateAsync(ct);
    }
}
