using Microsoft.Extensions.DependencyInjection;
using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure;

/// <summary>Dev convenience: ensures the PostgreSQL schema exists and seeds one coordinator.
/// Use real versioned migrations (Flyway/DbUp — standards 02 §5) for production.</summary>
public static class DbSchema
{
    public static async Task EnsureAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        var search = sp.GetRequiredService<SearchOptions>();
        var conn = sp.GetRequiredService<DbConnectionFactory>().Create();
        await using (conn)
        {
            if (conn.GetType().Name != "NpgsqlConnection") return; // dev helper targets Postgres
            await conn.OpenAsync(ct);

            var config = await ResolveTextSearchConfigAsync(conn, search.TextSearchConfig, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = Ddl + FullTextDdl(config);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Confirms the configured text-search configuration actually exists on this server, falling back
    /// to <c>simple</c> (which every installation has) rather than letting every search fail later.
    /// The name is shape-checked before it is used, and this lookup is parameterised.
    /// </summary>
    private static async Task<string> ResolveTextSearchConfigAsync(
        System.Data.Common.DbConnection conn, string requested, CancellationToken ct)
    {
        if (!SearchOptions.Validate(requested)) return "simple";

        await using var probe = conn.CreateCommand();
        probe.CommandText = "SELECT 1 FROM pg_ts_config WHERE cfgname = @name";
        var parameter = probe.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = requested;
        probe.Parameters.Add(parameter);

        return await probe.ExecuteScalarAsync(ct) is null ? "simple" : requested;
    }

    /// <summary>
    /// A stored, generated tsvector plus a GIN index over it — so keyword search is an index lookup
    /// rather than a sequential scan that re-tokenises every row. The configuration name is baked in
    /// because a generated column may only call an immutable expression, and
    /// <c>to_tsvector(regconfig, text)</c> qualifies only when the configuration is a literal.
    /// </summary>
    private static string FullTextDdl(string config) => $"""

        ALTER TABLE tickets ADD COLUMN IF NOT EXISTS search_tsv tsvector
            GENERATED ALWAYS AS (to_tsvector('{config}',
                coalesce(title, '') || ' ' || coalesce(description, '') || ' ' || coalesce(resolution, '')
            )) STORED;
        CREATE INDEX IF NOT EXISTS ix_tickets_search ON tickets USING GIN (search_tsv);
        """;

    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS users (
            id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            full_name     text NOT NULL,
            email         text NOT NULL UNIQUE,
            role          int  NOT NULL DEFAULT 0,
            skills        text,
            is_active     boolean NOT NULL DEFAULT true,
            created_at_utc timestamptz NOT NULL DEFAULT now()
        );
        -- Local-provider password storage. NULL for directory and OIDC accounts, which have none here.
        ALTER TABLE users ADD COLUMN IF NOT EXISTS password_hash text;
        -- TOTP secret, stored only once the person has proved they can generate a code from it.
        ALTER TABLE users ADD COLUMN IF NOT EXISTS totp_secret text;
        ALTER TABLE users ADD COLUMN IF NOT EXISTS totp_enabled boolean NOT NULL DEFAULT false;

        -- Single-use account setup links. Only the token's SHA-256 hash is kept: a database dump does
        -- not yield working links, and a link cannot be re-displayed after it is handed over once.
        CREATE TABLE IF NOT EXISTS user_invitations (
            id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            user_id         bigint NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            token_hash      text NOT NULL UNIQUE,
            expires_at_utc  timestamptz NOT NULL,
            consumed_at_utc timestamptz,
            created_by      bigint REFERENCES users(id) ON DELETE SET NULL,
            created_at_utc  timestamptz NOT NULL DEFAULT now()
        );
        -- At most one live invitation per account; re-inviting replaces the old link rather than
        -- leaving two valid ways in.
        CREATE UNIQUE INDEX IF NOT EXISTS ux_invitations_live
            ON user_invitations(user_id) WHERE consumed_at_utc IS NULL;

        -- Short-lived, single-use handles for sign-ins in flight: a pending second factor, a session
        -- crossing an SSO redirect, a WebAuthn challenge, an OIDC authorization. Shared rather than
        -- per-process, because each of these spans two requests that need not reach the same instance.
        CREATE TABLE IF NOT EXISTS auth_handles (
            handle_hash    text PRIMARY KEY,
            purpose        text NOT NULL,
            payload        text NOT NULL,
            expires_at_utc timestamptz NOT NULL,
            created_at_utc timestamptz NOT NULL DEFAULT now()
        );
        -- Sweeping on issue is a range scan over this, not a sequential scan over the table.
        CREATE INDEX IF NOT EXISTS ix_auth_handles_expiry ON auth_handles(expires_at_utc);

        -- WebAuthn credentials. The public key is public by design; there is no secret here to leak.
        CREATE TABLE IF NOT EXISTS user_passkeys (
            id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            user_id         bigint NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            credential_id   bytea NOT NULL UNIQUE,
            public_key      bytea NOT NULL,
            sign_count      bigint NOT NULL DEFAULT 0,
            label           text NOT NULL,
            created_at_utc  timestamptz NOT NULL DEFAULT now(),
            last_used_at_utc timestamptz
        );
        CREATE INDEX IF NOT EXISTS ix_passkeys_user ON user_passkeys(user_id);

        CREATE SEQUENCE IF NOT EXISTS ticket_ref_seq START 1;
        CREATE TABLE IF NOT EXISTS tickets (
            id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            reference      text NOT NULL UNIQUE,
            title          text NOT NULL,
            description    text NOT NULL,
            status         int  NOT NULL DEFAULT 0,
            priority       int  NOT NULL DEFAULT 1,
            source         int  NOT NULL DEFAULT 0,
            category       text,
            customer_name  text NOT NULL,
            customer_contact text,
            assignee_id    bigint REFERENCES users(id),
            coordinator_id bigint REFERENCES users(id),
            resolution     text,
            created_at_utc timestamptz NOT NULL DEFAULT now(),
            updated_at_utc timestamptz NOT NULL DEFAULT now(),
            sla_due_at_utc timestamptz,
            resolved_at_utc timestamptz
        );
        CREATE INDEX IF NOT EXISTS ix_tickets_status   ON tickets(status);
        CREATE INDEX IF NOT EXISTS ix_tickets_assignee ON tickets(assignee_id);
        CREATE INDEX IF NOT EXISTS ix_tickets_created  ON tickets(created_at_utc DESC);
        CREATE TABLE IF NOT EXISTS ticket_activities (
            id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            ticket_id      bigint NOT NULL REFERENCES tickets(id),
            author_id      bigint REFERENCES users(id),
            kind           int NOT NULL DEFAULT 0,
            body           text NOT NULL,
            created_at_utc timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS ix_activities_ticket ON ticket_activities(ticket_id, created_at_utc);

        -- Model output about a ticket. Separate from `tickets` on purpose: it is advisory, it can be
        -- regenerated at any time, and dropping the table must not lose anything a human wrote.
        CREATE TABLE IF NOT EXISTS ticket_assessments (
            ticket_id              bigint PRIMARY KEY REFERENCES tickets(id) ON DELETE CASCADE,
            difficulty             int  NOT NULL,
            estimated_minutes      int  NOT NULL,
            suggested_category     text,
            suggested_priority     int,
            duplicate_of_ticket_id bigint REFERENCES tickets(id) ON DELETE SET NULL,
            summary                text NOT NULL DEFAULT '',
            confidence             double precision NOT NULL DEFAULT 0,
            model                  text NOT NULL DEFAULT '',
            created_at_utc         timestamptz NOT NULL DEFAULT now()
        );

        -- Likewise derived: the agent's own resolution stays in tickets.resolution, untouched.
        CREATE TABLE IF NOT EXISTS resolution_reviews (
            ticket_id           bigint PRIMARY KEY REFERENCES tickets(id) ON DELETE CASCADE,
            polished_reply      text NOT NULL DEFAULT '',
            internal_note       text NOT NULL DEFAULT '',
            verdict             int  NOT NULL DEFAULT 2,
            verdict_detail      text NOT NULL DEFAULT '',
            compared_references text NOT NULL DEFAULT '',
            model               text NOT NULL DEFAULT '',
            created_at_utc      timestamptz NOT NULL DEFAULT now()
        );

        -- One stored routing analysis per ticket. The candidate list is jsonb because it is read and
        -- written whole, never queried by field — a child table would buy nothing but joins.
        CREATE TABLE IF NOT EXISTS ticket_routing (
            ticket_id      bigint PRIMARY KEY REFERENCES tickets(id) ON DELETE CASCADE,
            assignee_id    bigint REFERENCES users(id) ON DELETE SET NULL,
            assignee_name  text,
            reason         text NOT NULL,
            candidates     jsonb NOT NULL,
            created_at_utc timestamptz NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS notifications (
            id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            user_id        bigint NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            kind           int NOT NULL DEFAULT 0,
            ticket_id      bigint REFERENCES tickets(id) ON DELETE CASCADE,
            title          text NOT NULL,
            body           text NOT NULL DEFAULT '',
            created_at_utc timestamptz NOT NULL DEFAULT now(),
            read_at_utc    timestamptz
        );
        CREATE INDEX IF NOT EXISTS ix_notifications_user
            ON notifications(user_id, created_at_utc DESC);
        -- Partial index: the unread badge is the hot query and only ever looks at unread rows.
        CREATE INDEX IF NOT EXISTS ix_notifications_unread
            ON notifications(user_id) WHERE read_at_utc IS NULL;
        INSERT INTO users (full_name, email, role, skills)
            SELECT 'System Coordinator', 'coordinator@resolvedesk.local', 1, 'routing'
            WHERE NOT EXISTS (SELECT 1 FROM users WHERE email = 'coordinator@resolvedesk.local');
        """;
}
