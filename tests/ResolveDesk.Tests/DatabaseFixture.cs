using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ResolveDesk.Application;
using ResolveDesk.Infrastructure;

namespace ResolveDesk.Tests;

/// <summary>
/// Marks a test that needs a real PostgreSQL. Without one it reports as skipped rather than passing
/// silently, so a green run on a machine with no database cannot be mistaken for coverage.
///
/// Testcontainers would be the usual answer, but it needs a Docker daemon; this takes a connection
/// string instead, which works with a local server, a CI service container, or a container the
/// developer started by hand — and does not pretend to have been verified against Docker.
/// </summary>
public sealed class RequiresDatabaseFactAttribute : FactAttribute
{
    public RequiresDatabaseFactAttribute()
    {
        if (TestDatabase.AdminConnectionString is null) Skip = TestDatabase.SkipReason;
    }
}

/// <inheritdoc cref="RequiresDatabaseFactAttribute"/>
public sealed class RequiresDatabaseTheoryAttribute : TheoryAttribute
{
    public RequiresDatabaseTheoryAttribute()
    {
        if (TestDatabase.AdminConnectionString is null) Skip = TestDatabase.SkipReason;
    }
}

public static class TestDatabase
{
    public const string SkipReason =
        "Set RESOLVEDESK_TEST_DB to a PostgreSQL connection string to run the database tests.";

    /// <summary>The database these tests create and drop. Never point the variable at real data.</summary>
    public const string DatabaseName = "resolvedesk_test";

    /// <summary>
    /// Connection to the maintenance database, derived from the configured string. CREATE/DROP DATABASE
    /// cannot run while connected to the database in question, so the name is swapped for "postgres".
    /// </summary>
    public static string? AdminConnectionString { get; } = Build();

    private static string? Build()
    {
        var configured = Environment.GetEnvironmentVariable("RESOLVEDESK_TEST_DB");
        if (string.IsNullOrWhiteSpace(configured)) return null;
        try
        {
            return new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" }.ConnectionString;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static string TestConnectionString =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString!) { Database = DatabaseName }.ConnectionString;
}

/// <summary>
/// Drops and recreates <see cref="TestDatabase.DatabaseName"/> once per run, then builds the schema
/// through the application's own <see cref="DbSchema"/> — so the tests exercise the DDL that ships,
/// not a copy of it that could drift.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public bool Available => TestDatabase.AdminConnectionString is not null;
    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!Available) return;

        await using (var admin = new NpgsqlConnection(TestDatabase.AdminConnectionString))
        {
            await admin.OpenAsync();
            // Terminate stragglers from an interrupted run, or DROP DATABASE blocks.
            await Execute(admin,
                $"""
                SELECT pg_terminate_backend(pid) FROM pg_stat_activity
                WHERE datname = '{TestDatabase.DatabaseName}' AND pid <> pg_backend_pid();
                """);
            await Execute(admin, $"DROP DATABASE IF EXISTS {TestDatabase.DatabaseName};");
            await Execute(admin, $"CREATE DATABASE {TestDatabase.DatabaseName};");
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_CONNECTION_STRING"] = TestDatabase.TestConnectionString,
                // No models: these tests are about SQL, not about AI. Every AI path degrades to off.
                ["Ai:Enabled"] = "false",
                ["Search:TextSearchConfig"] = "english",
            })
            .Build());
        services.AddLogging();
        services.AddInfrastructure(services.BuildServiceProvider().GetRequiredService<IConfiguration>());

        Services = services.BuildServiceProvider();
        await DbSchema.EnsureAsync(Services);
    }

    public async Task DisposeAsync()
    {
        if (!Available) return;
        await Services.DisposeAsync();
        NpgsqlConnection.ClearAllPools();

        await using var admin = new NpgsqlConnection(TestDatabase.AdminConnectionString);
        await admin.OpenAsync();
        await Execute(admin,
            $"""
            SELECT pg_terminate_backend(pid) FROM pg_stat_activity
            WHERE datname = '{TestDatabase.DatabaseName}' AND pid <> pg_backend_pid();
            """);
        await Execute(admin, $"DROP DATABASE IF EXISTS {TestDatabase.DatabaseName};");
    }

    /// <summary>Empties every table so one test's rows cannot decide another test's ranking.</summary>
    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(TestDatabase.TestConnectionString);
        await conn.OpenAsync();
        await Execute(conn,
            """
            TRUNCATE notifications, resolution_reviews, ticket_assessments, ticket_activities,
                     tickets, users RESTART IDENTITY CASCADE;
            ALTER SEQUENCE ticket_ref_seq RESTART WITH 1;
            """);
    }

    public T Resolve<T>() where T : notnull => Services.GetRequiredService<T>();

    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(TestDatabase.TestConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    private static async Task Execute(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    // One database for the whole run; each test class resets it. Serialised because the tests share
    // a schema and ranking assertions depend on exactly which rows exist.
    public const string Name = "database";
}
