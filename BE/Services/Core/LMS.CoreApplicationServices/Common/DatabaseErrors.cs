using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LMS.CoreApplicationServices.Common;

public static class DatabaseErrors
{
    // Map only uniqueness conflicts. Foreign keys and unavailable databases are different failures.
    public static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode is 1555 or 2067,
        SqlException sqlServer => sqlServer.Number is 2601 or 2627,
        _ => false
    };
}
