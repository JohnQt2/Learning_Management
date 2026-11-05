using Microsoft.EntityFrameworkCore;

namespace LMS.Core.Infrastructure.Persistence;

// Separate migration snapshots keep SQLite and SQL Server column types independent.
public sealed class SqliteLmsDbContext(DbContextOptions<SqliteLmsDbContext> options) : LmsDbContext(options);
public sealed class SqlServerLmsDbContext(DbContextOptions<SqlServerLmsDbContext> options) : LmsDbContext(options);
