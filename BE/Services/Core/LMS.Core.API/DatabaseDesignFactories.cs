using LMS.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LMS.Core.API;

// Scaffolding and script generation do not start the web host or touch a live database.
public sealed class SqliteDesignFactory : IDesignTimeDbContextFactory<SqliteLmsDbContext>
{
    public SqliteLmsDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqliteLmsDbContext>().UseSqlite("Data Source=design-only.sqlite").Options);
}
public sealed class SqlServerDesignFactory : IDesignTimeDbContextFactory<SqlServerLmsDbContext>
{
    public SqlServerLmsDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqlServerLmsDbContext>().UseSqlServer(
            "Server=(localdb)\\MSSQLLocalDB;Database=LMSAI_DesignOnly;Integrated Security=True").Options);
}
