# Administrator guide

Running and configuring a deployment.

## Configuration

Every key lives in `appsettings.json` and can be overridden by an environment variable using the
double-underscore form (`Ai__Chat__Provider`). The full list is in `.env.example`. **No secret is ever
written to a file in the repository.**

### AI — chat and embedding are configured separately

```jsonc
"Ai": {
  "Enabled": true,
  "MinSimilarity": 0.50,
  "Chat":      { "Provider": "Ollama", "BaseUrl": "http://localhost:11434", "Model": "qwen2.5-coder:7b" },
  "Embedding": { "Provider": "Ollama", "BaseUrl": "http://localhost:11434", "Model": "nomic-embed-text", "Dimensions": 768 }
}
```

`Provider` is `None | Ollama | OpenAi | AzureOpenAi | Gemini`. `OpenAi` covers every OpenAI-compatible
server — LM Studio, vLLM, llama.cpp, OpenRouter — just point `BaseUrl` at them. Chat can be hosted
while embedding stays local, or the reverse.

> `Dimensions` **must** match what the model emits; the pgvector column is fixed-width. A mismatch
> produces a clear error. Changing it means a migration and a re-embed.

`MinSimilarity` is a **floor, not a separator**, and its scale belongs to the embedding model rather
than to this product — see [decisions](decisions.md#adr-011). After changing models, re-measure:

```bash
node tools/validate-retrieval.mjs
```

### Full-text language

```jsonc
"Search": { "TextSearchConfig": "english" }
```

Baked into the generated `tickets.search_tsv` column, so changing it after the schema exists requires
a migration. Use `simple` for a language PostgreSQL has no stemmer for.

### Authentication

Sign-in methods are a **set**, not a choice. Turn on as many as you want; the sign-in screen offers
all of them.

```jsonc
"Auth": {
  "Enabled": true,
  "Methods": [ "Password", "Passkey", "Oidc" ],
  "Provider": "Ldap",                  // where a password is checked: Local | Ldap
  "Totp":     { "Enabled": true, "Required": false },
  "Passkey":  { "RelyingPartyId": "desk.example.org", "Origins": [ "https://desk.example.org" ] },
  "Invitations": { "LifetimeHours": 48, "BaseUrl": "https://desk.example.org" },
  "Ldap": {
    "Host": "dc01.example.org", "Port": 636, "UseSsl": true,
    "BaseDn": "DC=example,DC=org",
    "BindDnTemplate": "{0}@example.org",
    "CoordinatorGroup": "CN=ResolveDesk-Coordinators",
    "AdminGroup": "CN=ResolveDesk-Admins",
    "AutoProvisionUsers": true
  }
}
```

- **Password** — checked against the local `users` table (PBKDF2-SHA256, 210,000 iterations) or an
  LDAP bind. On the LDAP path the successful bind *is* the proof; no password is stored here, and
  group membership maps to a role.
- **Passkey** — WebAuthn. `RelyingPartyId` must be the registrable domain and `Origins` the exact
  origins the browser will report, or every assertion is rejected. That strictness is the feature.
- **TOTP** — an optional second factor after a password. `Required: true` refuses to sign anybody in
  until they have enrolled one.

`Auth__Jwt__SigningKey` must be at least 32 bytes and is **mandatory outside Development** — the
application refuses to start without it. Generate one with `openssl rand -base64 48`.

`DB_CONNECTION_STRING` behaves the same way: there is a localhost default in Development and nowhere
else, so a misconfigured production instance fails loudly instead of connecting somewhere unintended.

### Single sign-on, in two shapes

Set `Oidc:ClientId` and **this API runs the flow itself** — authorization code with PKCE, the ID token
validated against the provider's JWKS, and then a ResolveDesk token issued. That last step is what
lets SSO sit beside a password and a passkey.

```jsonc
"Oidc": {
  "Authority": "https://id.example.org/realms/main",
  "ClientId": "resolvedesk",
  "ClientSecret": null,                 // omit for a public client; PKCE is used either way
  "RedirectUri":  "https://desk.example.org/api/v1/auth/oidc/callback",
  "PostLoginUrl": "https://desk.example.org/auth/callback",
  "RoleClaim": "roles",
  "CoordinatorRole": "resolvedesk-coordinators",
  "AdminRole": "resolvedesk-admins"
}
```

Leave `ClientId` empty and it reverts to validating a provider-issued token that the SPA obtained
itself. The two disagree about whose token is trusted, so they are mutually exclusive — **the first
log line at startup says which one is active.** Check it.

## Accounts

**There is no default account and no self-registration.** The first administrator is created once:

```
Auth__BootstrapAdminEmail=admin@example.org
Auth__BootstrapAdminPassword=<a strong password>
```

The account is created on the next start and a warning is logged. **Change the password after the
first sign-in and remove both variables from the environment.**

After that, accounts are created from **Settings → Team**. The new person receives a single-use setup
link. Only the token's SHA-256 hash is stored, so the URL is shown exactly once — the product cannot
show it again, because it no longer knows it — and opening it burns it. Re-issuing a link invalidates
the previous one.

Roles: `Agent` works tickets, `Coordinator` also routes them and creates accounts, `Admin` also
manages roles.

## External services

| Service | Purpose | Without it |
|---|---|---|
| PostgreSQL | Everything | The application does not run |
| pgvector | Semantic search | Falls back to keyword search |
| Chat model | Drafted answers, assessment, review | Matches are returned without a draft |
| Embedding model | Vector search | Falls back to keyword search |
| LDAP / OIDC | Authentication | The local provider is used |

Every one of these degrades rather than failing. That is deliberate and it is worth testing: switch
`Ai:Enabled` to false and confirm the product still works.

## Running more than one instance

Sign-in state — MFA handles, passkey challenges, OIDC states, SSO handoff codes — is shared through
the `auth_handles` table, so a sign-in started on one instance finishes on another. Every instance
must use the **same `Auth__Jwt__SigningKey`**, or a token issued by one is meaningless to the next.

Two things remain per-process:

- **The sign-in rate limiter**, so N instances allow N× the attempts. Limit at the load balancer, or
  add Redis.
- **The notification hub.** A client connected to node A misses a live push originating on node B and
  picks it up from the database on the next load — degradation, not loss.

## Monitoring

- `GET /health/live` — the process is alive
- `GET /health/ready` — it can accept traffic (the database answered)
- `GET /api/v1/ai/status` — **look here first**: which provider, which model, whether it is reachable,
  and how much of the archive is indexed. **An API key is never shown.**
- `GET /api/v1/knowledge/stats` — index coverage

Logs are Serilog compact JSON on stdout, ready for a collector as-is.

## Backup

PostgreSQL is the only thing to back up. `ticket_embeddings` is **derived data** and can be excluded —
the background worker rebuilds it from the model. `auth_handles` holds nothing that outlives a
sign-in; losing it costs whoever was mid-authentication one retry.
