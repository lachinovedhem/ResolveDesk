# Developer guide

## Architecture

```
  Browser (React SPA) ──► ResolveDesk.WebApi       (Native AOT, minimal API)
                                 │
                                 ▼
                          ResolveDesk.Application   (ports — no dependencies)
                                 │
                                 ▼
                          ResolveDesk.Infrastructure
                          ├── Dapper.AOT ──► PostgreSQL (+ pgvector)
                          ├── AiChatClient / AiEmbeddingClient ──► model
                          └── Local | LDAP | OIDC identity, backfill worker

  MCP client ──► resolvedesk-mcp (stdio) ──► WebApi (HTTP)
                 └── Semantic Kernel triage agent
```

Dependencies point inward only. `Core` is pure records; `Application` declares ports and DTOs;
`Infrastructure` is the only project that knows about PostgreSQL, HTTP or a directory server.

## Stack

- **API** — .NET 10, Native AOT, minimal API (`CreateSlimBuilder`), source-generated JSON, Serilog, OpenAPI
- **Data** — PostgreSQL + Dapper.AOT; pgvector with an HNSW index (`vector_cosine_ops`)
- **AI** — our own HTTP clients: Ollama, OpenAI, Azure OpenAI, Gemini, anything OpenAI-compatible
- **MCP** — `ModelContextProtocol`, `Microsoft.SemanticKernel` (this project is **not** AOT — see
  [ADR-005](decisions.md#adr-005))
- **Web** — React 19, Vite, Tailwind, AG Grid, lucide-react

## Database

Connection string comes from `DB_CONNECTION_STRING`. There is a localhost default in Development and
nowhere else.

| Table | Purpose |
|---|---|
| `users` | The team. `password_hash` is NULL for directory and OIDC accounts |
| `tickets` | The queue. `reference` is generated from `ticket_ref_seq` inside the INSERT |
| `ticket_activities` | Timeline: comments, status changes, assignments, resolutions |
| `ticket_embeddings` | `vector(N)` + `content_hash` + `model`; HNSW index |
| `ticket_assessments` | Difficulty, effort, category, duplicate — one row per ticket |
| `ticket_routing` | The stored routing analysis; candidates as `jsonb` |
| `resolution_reviews` | The tidied reply and the comparison verdict |
| `notifications` | Durable; SSE is only the delivery |
| `user_invitations` | Single-use setup links, stored as a SHA-256 hash |
| `user_passkeys` | WebAuthn credential ids and public keys |
| `auth_handles` | Short-lived, single-use handles shared across instances |

`DbSchema.EnsureAsync` is **a development convenience**. Use versioned migrations (DbUp, Flyway) in
production.

## The retrieval pipeline

```
new ticket ──► embed (N dims) ──► pgvector: ORDER BY embedding <=> query      ──┐
           └─► lexemes OR-joined ──► ts_rank_cd(search_tsv, query, 36), GIN ────┤
                                                                                ▼
                                        reciprocal rank fusion (k = 60)
                                                                                ▼
                                        top N resolutions ──► drafted answer
```

Cosine similarity and `ts_rank_cd` are not on the same scale, so the scores are never added — RRF uses
only the ordinal positions. The `strategy` field in the response reports which path ran.

Two details that are easy to get wrong, and were:

- The lexical query **ORs** its terms. `plainto_tsquery` builds an AND, which for a whole ticket body
  matches nothing. See [ADR-014](decisions.md#adr-014).
- The normalisation flag is `36`, chosen by measurement rather than by default. Same ADR.

**Degradation** is the design: no embedding model → lexical; no pgvector → lexical; no chat model →
matches without a draft; nothing configured → the product still works.

## Layout

```
src/ResolveDesk.Core            domain records
src/ResolveDesk.Application     ports, DTOs, options
src/ResolveDesk.Infrastructure  Dapper.AOT, pgvector, AI clients, auth, background workers
src/ResolveDesk.WebApi          minimal API (Native AOT)
src/ResolveDesk.Mcp             MCP server + Semantic Kernel triage agent
frontend/                       React SPA
tests/ResolveDesk.Tests         70 tests; database tests skip without RESOLVEDESK_TEST_DB
tools/                          smoke tests, the demo seeder, the retrieval harness
```

## Tools

| Tool | What it does |
|---|---|
| `tools/seed-demo.mjs` | Seeds an ISP archive and demonstrates retrieval against it |
| `tools/validate-retrieval.mjs` | Measures threshold and ranking — **no database**, just an embedding model |
| `tools/mcp-smoke.mjs` | MCP protocol test |
| `tools/auth-smoke.mjs` | 42 checks: invitations, TOTP, passkey challenges, rate limiting |
| `tools/fake-oidc-provider.mjs` | A real OIDC provider: RSA keys, signed tokens, PKCE enforced |
| `tools/oidc-smoke.mjs` | 25 checks: the whole SSO flow end to end |
| `tools/multi-instance-smoke.mjs` | 15 checks across two instances — start on A, finish on B |

## Tests

```bash
dotnet test                                    # unit tests; database tests report as skipped
RESOLVEDESK_TEST_DB="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres" \
  dotnet test                                  # 70 tests, database included
```

The variable names a **server**, not a database to use: the fixture creates and drops
`resolvedesk_test` around the run and builds its schema through the application's own `DbSchema`, so
the tests exercise the DDL that ships. Without it the database tests report as **skipped**, never as
passed, and CI fails the build if the skipped count is not zero.

Each regression test was run with its fix reverted and kept only once it failed. Two did not, at
first — one had a vacuous assertion, the other seeded data where equal scores were the *correct*
answer — and both were rewritten until reintroducing the bug turned them red.

## API surface

```
POST   /api/v1/auth/login              POST /api/v1/auth/mfa
GET    /api/v1/auth/config             GET  /api/v1/auth/me       POST /api/v1/auth/password
GET    /api/v1/auth/oidc/start         GET  /api/v1/auth/oidc/callback
POST   /api/v1/auth/oidc/exchange
POST   /api/v1/auth/passkey/begin      POST /api/v1/auth/passkey/finish

POST   /api/v1/tickets                 GET  /api/v1/tickets       GET /api/v1/tickets/{id}
POST   /api/v1/tickets/{id}/assign     POST /api/v1/tickets/{id}/status
GET    /api/v1/tickets/{id}/activities POST /api/v1/tickets/{id}/comments
GET    /api/v1/tickets/{id}/suggestions
GET    /api/v1/tickets/{id}/assessment POST /api/v1/tickets/{id}/assessment
GET    /api/v1/tickets/{id}/routing    POST /api/v1/tickets/{id}/routing
GET    /api/v1/tickets/{id}/resolution-review

POST   /api/v1/invitations             GET  /api/v1/invitations
POST   /api/v1/invitations/{id}/resend DELETE /api/v1/invitations/{id}
GET    /api/v1/invite/{token}          POST /api/v1/invite/{token}

GET    /api/v1/account/totp            POST /api/v1/account/totp/setup|enable|disable
GET    /api/v1/account/passkeys        DELETE /api/v1/account/passkeys/{id}
POST   /api/v1/account/passkeys/register/begin|finish

POST   /api/v1/knowledge/search        GET  /api/v1/knowledge/stats
GET    /api/v1/users                   POST /api/v1/users
GET    /api/v1/notifications           GET  /api/v1/notifications/stream
GET    /api/v1/stats                   GET  /api/v1/ai/status
GET    /health/live                    GET  /health/ready         GET /openapi/v1.json
```

## AOT rules

Mandatory in this repository, because the value of a zero-warning build is that people still read the
warnings:

- **Never serialise an anonymous type.** Add a record to `AppJsonContext`.
- **Never use `Results.Json<T>(value)`.** Use the `JsonTypeInfo` overload.
- No reflection-based options binding — `AiOptions.Read` and `AuthOptions.Read` read keys explicitly.
- JWT bearer is the only authentication handler AOT supports. Do not add another.
- The build must be **zero warnings**. The analyser runs on every build; a warning is a defect.

Two Dapper.AOT traps worth knowing, both of which cost time here:

- A row type nested inside the repository class makes the generator emit `Type expected`. Declare it
  in the namespace.
- An inline `JsonSerializer.Serialize(...)` inside an anonymous parameter object becomes an untyped
  `default()`. Assign it to a `string` local first.

## Building and running

```bash
dotnet publish src/ResolveDesk.WebApi -c Release -p:PublishAot=true
```

On Windows this needs Visual Studio's "Desktop development with C++" workload for `link.exe`.

```bash
dotnet run --project src/ResolveDesk.WebApi --no-launch-profile
cd frontend && npm run dev
node tools/seed-demo.mjs
```

Or `docker compose up -d` for PostgreSQL with pgvector already built in.
