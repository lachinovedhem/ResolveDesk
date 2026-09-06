using System.Data.Common;

namespace ResolveDesk.Infrastructure;

public sealed class DbConnectionFactory(string connectionString)
{
    private readonly string _cs = connectionString;
    public DbConnection Create() => new Npgsql.NpgsqlConnection(_cs);
}
