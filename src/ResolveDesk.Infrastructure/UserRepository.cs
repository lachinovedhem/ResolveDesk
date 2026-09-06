using Dapper;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure;

public sealed class UserRepository(DbConnectionFactory factory) : IUserRepository
{
    private const string Cols =
        @"id AS ""Id"", full_name AS ""FullName"", email AS ""Email"", role AS ""Role"",
          skills AS ""Skills"", is_active AS ""IsActive"", created_at_utc AS ""CreatedAtUtc""";

    public async Task<long> CreateAsync(UserCreate input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO users (full_name, email, role, skills) VALUES (@fullName, @email, @role, @skills)
            RETURNING id;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(sql, new { fullName = input.FullName, email = input.Email, role = (int)input.Role, skills = input.Skills });
    }

    public async Task<User?> GetAsync(long id, CancellationToken ct = default)
    {
        var sql = $"SELECT {Cols} FROM users WHERE id = @id";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<User>(sql, new { id });
    }

    public async Task<IReadOnlyList<User>> ListAsync(UserRole? role, CancellationToken ct = default)
    {
        var sql = $"SELECT {Cols} FROM users WHERE (@role IS NULL OR role = @role) AND is_active ORDER BY full_name";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<User>(sql, new { role = (int?)role })).ToList();
    }
}

public sealed class ActivityRepository(DbConnectionFactory factory) : IActivityRepository
{
    public async Task<long> AddAsync(long ticketId, long? authorId, ActivityKind kind, string body, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO ticket_activities (ticket_id, author_id, kind, body) VALUES (@ticketId, @authorId, @kind, @body)
            RETURNING id;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(sql, new { ticketId, authorId, kind = (int)kind, body });
    }

    public async Task<IReadOnlyList<TicketActivity>> ListAsync(long ticketId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS "Id", ticket_id AS "TicketId", author_id AS "AuthorId", kind AS "Kind",
                   body AS "Body", created_at_utc AS "CreatedAtUtc"
            FROM ticket_activities WHERE ticket_id = @ticketId ORDER BY created_at_utc;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<TicketActivity>(sql, new { ticketId })).ToList();
    }
}
