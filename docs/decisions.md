# Decision log

Twenty decisions, each with the reasoning that produced it and — where a number was involved — the
measurement that settled it. Several record being wrong first: a threshold that filtered nothing, a
demo dataset that flattered itself, a lexical query that matched nothing at all. Those are kept
deliberately. A decision log that only contains decisions that turned out well is a marketing
document.

---

<a id="adr-001"></a>

## ADR-001 — Platform and architecture

**Decision.** Clean Architecture, Native AOT, Dapper.AOT, minimal API. PostgreSQL. Authentication
optional.

Dependencies point inward only: `Core` is pure records, `Application` declares the ports,
`Infrastructure` is the only project that knows about PostgreSQL, HTTP or a directory server.

<a id="adr-002"></a>

## ADR-002 — Our own HTTP layer for AI providers, not Microsoft.Extensions.AI

**Context.** The API is Native AOT, and several providers — local and hosted — must be selectable from
configuration.

**Decision.** A thin HTTP client per provider with source-generated JSON (`AiJsonContext`). The
`OpenAi` provider covers every OpenAI-compatible server: LM Studio, vLLM, llama.cpp, OpenRouter.

**Why.** `Microsoft.Extensions.AI` and the OpenAI SDK emit trimming and AOT warnings, while the wire
formats themselves are three endpoint shapes and entirely stable. Writing them out keeps the build at
zero warnings, which is the property that makes the AOT analyser useful at all — one accepted warning
and nobody reads the rest.

**Consequence.** A new provider is one `switch` arm and one wire record. Chat and embedding are
configured **separately**, so one can be local and the other hosted.

<a id="adr-003"></a>

## ADR-003 — Hybrid retrieval, fused with reciprocal rank fusion

**Decision.** Semantic results (cosine, HNSW) and lexical results (`ts_rank_cd`) are merged with
**reciprocal rank fusion**, k = 60.

**Why.** Cosine similarity and `ts_rank` are not on the same scale, so they cannot be added. RRF uses
only the ordinal positions, and a ticket that appears in both lists rises above one found by either.
See ADR-014 for what those two scores actually mean — they are less comparable than they look.

**Consequence.** A `strategy` field reports which path ran, so the UI never implies more than
happened. Without pgvector, or without a model, the product still answers.

<a id="adr-004"></a>

## ADR-004 — Embeddings in the background, keyed on a content hash

**Decision.** `EmbeddingBackfillService` finds rows whose `md5(title + description + resolution)` no
longer matches what was indexed, and re-embeds them.

**Why.** Three requirements collapse into one query: closing a ticket must not wait on a model; an
index built after the fact must fill itself in; and an edited resolution must be re-embedded. A hash
comparison satisfies all three without a queue, a flag column or a migration.

<a id="adr-005"></a>

## ADR-005 — The MCP server is a separate, non-AOT project, and Semantic Kernel lives there

**Context.** Semantic Kernel uses reflection for function calling, which is incompatible with AOT.

**Decision.** `ResolveDesk.Mcp` is its own console project (`PublishAot=false`) that talks to the API
over **HTTP**. The triage tool is an SK agent.

**Why.** The API serves every request, so AOT pays for itself there; the MCP server is a short-lived
stdio process, where startup time is irrelevant. Going over HTTP also means one binary can point at
any instance — local, staging, production — and no SQL is duplicated between the two.

<a id="adr-006"></a>

## ADR-006 — Only the credential check changes between auth providers

**Decision.** `IIdentityValidator` has a local and an LDAP implementation, and **both issue the same
JWT**.

**Why.** JWT bearer is the only authentication handler Native AOT supports — the ASP.NET Core
compatibility table lists "Other Authentication ❌". Converging every method on one token keeps
authorization, roles and expiry identical no matter how somebody signed in. ADR-019 extends the same
principle to SSO.

**Consequence.** Moving from local accounts to a corporate directory is one configuration value. The
product ships with **no default account**; the first admin is created once from
`Auth__BootstrapAdminEmail` / `Password`.

<a id="adr-007"></a>

## ADR-007 — AG Grid is lazy-loaded and conditionally rendered, not hidden with CSS

**Decision.** At ≥1024px the queue is an AG Grid; below that it is tile cards. Conditional render plus
`React.lazy`, not `display: none`.

**Why.** A hidden grid still costs its bundle and its DOM. Measured: the grid chunk is 1,034 kB
(290 kB gzipped) and loads separately; a phone downloads 247 kB + 50 kB and never touches it.

<a id="adr-008"></a>

## ADR-008 — Routing is arithmetic, not a model

**Context.** "Who should take this" could have been asked of an LLM.

**Decision.** Three measurable signals: who has resolved similar tickets (0.5), skill match (0.3),
available capacity (0.2). All three numbers are shown next to every candidate.

**Why.** A coordinator who can see the numbers will **overrule** a recommendation when it is wrong. A
model's verdict is either accepted blindly or ignored entirely, and neither is useful. Arithmetic is
also testable. The model contributes upstream instead, as the difficulty and category.

**Consequence.** When every signal is zero the reason says so — *"no track record or skill match for
this subject; they simply have the lightest queue"* — rather than dressing up "least busy" as a match.

<a id="adr-009"></a>

## ADR-009 — Model output is not trusted as structured data

**Context.** Assessment and resolution review ask the model for JSON. Local models wrap it in a code
fence, prefix it with a sentence, and append a closing pleasantry.

**Decision.** `ModelJson.Extract` pulls the first balanced `{…}` using brace matching that tracks
string state. Every field is nullable; every number is clamped.

**Why.** Demanding a clean response would make the feature flaky on exactly the local setup this
product is built around. And a stray `difficulty: 99` must not be able to poison the queue ordering.

<a id="adr-010"></a>

## ADR-010 — Notifications over SSE, but not via EventSource

**Decision.** Notifications are durable in PostgreSQL; live delivery is server-sent events. The client
uses `fetch` + `ReadableStream` rather than `EventSource`.

**Why.** `EventSource` cannot send an `Authorization` header. The alternative is a token in the URL,
which lands in proxy logs and browser history. Reconnection becomes our responsibility — bounded
exponential backoff — which is the price.

**Consequence.** The hub is per-process. In a multi-instance deployment a client connected to node A
misses a live push originating on node B and picks it up from the database on the next load:
degradation, not loss. Moving to Redis pub/sub is a one-class change.

<a id="adr-011"></a>

## ADR-011 — MinSimilarity was measured, not assumed, and then moved into configuration

**Context.** `MinSimilarity = 0.35` came from a paper. That makes it a guess.

**Measurement.** `tools/validate-retrieval.mjs` — no database, just an embedding model. At 0.35,
**35 of 36** unrelated pairs cleared the threshold: the filter was doing nothing. `nomic-embed-text`
compresses its scores into roughly 0.33–0.74.

**The finding that mattered.** The two populations **overlap** — the weakest true match (0.582) scores
below the strongest distractor (0.630). So no threshold can separate them. Ranking does the
discrimination; the threshold is only a floor.

| threshold | true kept | distractors through | headroom |
|---|---|---|---|
| 0.40 | 7/7 | 57/63 | 0.182 |
| 0.45 | 7/7 | 42/63 | 0.132 |
| **0.50** | **7/7** | **18/63** | **0.082** ← default |
| 0.55 | 7/7 | 12/63 | 0.032 |

0.55 removes six more distractors for half the safety margin — a poor trade on a seven-query sample.

**Consequence.** The value moved to `Ai:MinSimilarity`, because the scale belongs to the **embedding
model**, not to this product. Re-run the harness after changing models.

<a id="adr-012"></a>

## ADR-012 — The demo data was changed because it was flattering itself

**Context.** The first four open tickets ranked first on keyword overlap alone. The demo was not
demonstrating what semantic search adds; it was restating it.

**Decision.** Three tickets were added, worded the way a customer speaks, sharing almost no vocabulary
with the engineer's write-up — *"callers tell me I sound like I am underwater"* against *"low
insulation resistance on the B leg — water in the joint box"*.

**Consequence.** Term overlap now misses rank 1 on **3 of 7**. That gap is the feature.

**Also.** The corpus moved to `tools/demo-corpus.mjs`, shared by the seeder and the harness, so the
data the demo shows and the data the measurement uses cannot drift apart.

<a id="adr-013"></a>

## ADR-013 — Domain types use DateTime (UTC), not DateTimeOffset

**Context.** The first run against a real PostgreSQL returned 500 from `GET /users`:
`InvalidCastException: Invalid cast from 'System.DateTime' to 'System.DateTimeOffset'`.

**Cause.** The columns were right (`timestamptz`). Npgsql returns `timestamptz` as
**`DateTime(Kind = Utc)`**, and Dapper.AOT does not recognise `DateTimeOffset` on its generated fast
path — it falls back to `Convert.ChangeType`, which cannot make that conversion.

**Worth noting.** This appeared only at **runtime**. The build was clean, zero warnings. No compiler
can catch it.

**Decision.** `DateTime` (UTC) throughout. A `timestamptz` column already means "an instant in UTC",
and the property names say `...AtUtc`, so the offset was redundant.

**Scope.** 23 occurrences across 14 files, including the JWT expiry — otherwise login would return
`+00:00` while tickets returned `Z`, and one API would have two conventions.

<a id="adr-014"></a>

## ADR-014 — Lexical search: OR semantics, a generated tsvector, and a measured normalisation

**Context.** Against a real database the first demo returned **nothing** (`strategy: none`), and then,
after a fix, returned **everything at 100%** in the wrong order. Three separate bugs.

**1. `plainto_tsquery` builds an AND.** For a thirty-word ticket body a candidate would have to
contain *every* word, which never happens. Fixed by running the text through `to_tsvector` and joining
its lexemes with `|`. That also sanitises: whatever the customer typed comes back as lexemes, never as
tsquery operators. `NULLIF` guards the all-stopwords case, since `to_tsquery('')` raises a syntax
error.

**2. `ts_rank_cd(...) * 10.0` saturated.** With `LEAST(1.0, …)` every score collapsed onto 1.0 and
`ORDER BY` fell through to `resolved_at_utc` — results were sorted by date, presented as relevance.

**3. The normalisation had never been chosen.** Measured against seven queries with known answers:
flags `0, 1, 2, 8, 16, 32, 33` put the right ticket first for 5–6 of 7; **`4` and `36` (= 4 | 32)
managed 7 of 7**. Flag 4 divides by the mean harmonic distance between term occurrences, rewarding
documents where the query's terms appear *near each other* — which is what "same problem, different
words" looks like. Flag 32 then maps the result into [0,1) monotonically, so the ordering survives.

**Performance.** `tickets.search_tsv` is a generated STORED column with a GIN index; previously every
query re-tokenised every row. The language is configurable, but the value is baked into the schema —
a generated column may only call an immutable expression — so changing it needs a migration.

**The consequence that matters.** This score is a **relevance**, not a similarity: 0.04 can be a
decisive first place, and it is not comparable with a cosine. Which is exactly why RRF fuses by rank
rather than by score (ADR-003).

<a id="adr-015"></a>

## ADR-015 — pgvector is built from source, not downloaded as a binary

**Context.** PostgreSQL 18 on Windows does not ship pgvector. The options were a community-built DLL
or a build from source.

**Decision.** From source: `github.com/pgvector/pgvector` at release tag **v0.8.6** (not master), with
MSVC and `nmake /F Makefile.win`.

**Why.** Placing a binary from an unverified source into `C:\Program Files` means code executing
*inside* the database server. Source can be read and a release tag can be checked.

**Reverting.** Delete `lib\vector.dll` and `share\extension\vector*`.

**Measured result.** Same seven queries, same machine, the only difference being whether pgvector was
installed: keyword only **6/7**, hybrid **7/7**. The case that separates them is instructive — two
tickets tie at `rel 0.091` and the tie-break falls through to the resolution date, picking the wrong
one. The vector half scores them 59% and 57%. So the honest case for an embedding model is not that
keyword search fails, but that **it goes flat exactly where two tickets use the same words for
different problems**.

<a id="adr-016"></a>

## ADR-016 — Intl.RelativeTimeFormat is not used for relative time

**Context.** Running the UI in a browser showed "Due in in 7 hours" on the tile cards.

**First bug.** `Intl.RelativeTimeFormat` already returns a complete phrase ("in 7 hours"), and the
template added "Due in" on top of it.

**Second, worse bug.** Chromium ships no relative-time data for `az`:
`new Intl.RelativeTimeFormat("az").format(7, "hour")` returns **"+7 h"**. The Azerbaijani UI was
displaying "+7 h qalıb". Visible only in a browser, and only in one language.

**Decision.** `formatDuration()` returns the **quantity alone** — "7 saat", "3 days" — with no
preposition and no direction. Each locale's own string supplies the framing: English `"Due in {time}"`
and `"{time} ago"`; Azerbaijani `"{time} qalıb"`, `"{time} əvvəl"`, `"{time} gecikib"`.

**Why.** Azerbaijani puts the direction *after* the quantity, so an API returning a finished phrase
cannot be composed into a correct sentence in both languages. Azerbaijani also does not pluralise a
noun after a numeral ("7 saat") while English does — another decision that belongs to the locale.

**Also fixed.** A resolved or closed ticket no longer shows an SLA countdown. A finished job's
deadline means nothing.

<a id="adr-017"></a>

## ADR-017 — The routing result is stored; re-running it is an explicit action

**Context.** `GET /tickets/17/routing` took 13 s, then 6.7 s.

**Cause.** `RoutingService` called the suggestion service **with drafting enabled**, so every request
had the model write a customer-facing answer that nothing read. The same mistake was in
`AssessmentService` and `ResolutionReviewService`.

**Decision.** `draftAnswer: false` in all three, and the result is stored in a `ticket_routing` table
(candidates as `jsonb`, since they are read and written whole). `GET` returns what is stored,
computing once on first view; `POST` re-runs it.

**Measured.** 13 s → 3.0 s for the first computation → **2 ms** for every read after that.

**In the UI.** A "Re-run" button and "analysed N ago", so a stale answer is visibly stale.

**Note.** Dapper.AOT cannot infer the type of an inline `JsonSerializer.Serialize(...)` inside an
anonymous object and emits an untyped `default()`. Assign it to a `string` local first.

<a id="adr-018"></a>

## ADR-018 — Sign-in methods are a set, not a choice

**Context.** `Auth:Provider` selected **one** method. A real team may need SSO for staff, a password
for a contractor, and a passkey for whoever has enrolled one — at the same time.

**Decision.** `Auth:Methods` is a flag set: `[Password, Oidc, Passkey]`. `Provider` now says only
**where a password is checked** (Local or Ldap). An older configuration is translated automatically,
so existing deployments keep working untouched.

**Account creation.** There is **no self-registration**. Someone who already has an account creates
the next one, and the new person receives a **single-use link** rather than a password chosen for
them. Only the token's SHA-256 hash is stored, so a database dump yields no working links and the
product cannot show the URL twice — it no longer knows it. Opening the link burns it.

**Two-factor.** TOTP (RFC 6238) written out by hand: the algorithm is thirty lines, and a package that
pulls in reflection would cost the API its clean AOT build. Enrolment is two steps — the secret is
stored, but the factor stays **off** until a code proves the authenticator holds it. Otherwise one
mistyped scan locks someone out of their own account.

**Passkeys.** WebAuthn, ES256 and RS256. **Attestation is not verified.** It answers "what make of
authenticator is this", which matters when only certified hardware may be used; for a support desk it
buys nothing and costs a metadata service and a certificate chain. Everything carrying the actual
guarantee — challenge, origin, relying party, the signature over both, and a signature counter that
must advance — is checked.

<a id="adr-019"></a>

## ADR-019 — The interactive OIDC flow runs server-side, and the API issues its own token

**Context.** OIDC existed only as "validate the provider's token". The SPA's SSO button pointed at a
`/auth/oidc/start` that did not exist.

**Decision.** Authorization code with **PKCE (S256)** executed server-side, the ID token validated
against the provider's JWKS, and then a **ResolveDesk** token issued.

**Why issue our own.** Without it SSO could not sit beside a password and a passkey: each method would
return a different token format, and roles and expiry would work two different ways. This is ADR-006's
"every method issues the same JWT" applied to SSO.

**Two shapes, mutually exclusive.** With `ClientId` set the flow is interactive; without it, the older
passthrough. They disagree about whose token is trusted, so the **first log line at startup** says
which is active — a misconfiguration would otherwise surface only when somebody tried to sign in.

**Token handoff.** The final redirect carries a **60-second single-use code**, not the token. A query
string lands in proxy logs and a fragment lands in browser history; a one-minute single-use code is
worth far less in either place than an eight-hour session.

**PKCE even with a client secret.** The secret proves *which client* is exchanging the code; PKCE
proves it is *the same party that started the flow*.

**Verification.** `tools/fake-oidc-provider.mjs` — real RSA keys, signed tokens, PKCE **enforced**.
Without it, JWKS retrieval, RS256 validation, the nonce check and PKCE — the parts that fail
*silently* when wrong — would have gone untested entirely. `tools/oidc-smoke.mjs` → 25/25.

**A bug only the browser found.** `OidcCallback` read the query string inside the effect. StrictMode
runs effects twice; the first run stripped the code out of the URL and the second read an empty query
and reported "that link was incomplete" — over a sign-in that had already succeeded. The query is now
captured at module load and the exchange fires once, guarded by a ref. No script test could have
caught it.

<a id="adr-020"></a>

## ADR-020 — Sign-in state is shared in PostgreSQL, not held in memory

**Context.** MFA handles, passkey challenges, OIDC states and SSO handoff codes lived in process
memory. All four span **two requests**, and behind a load balancer the second one lands on a different
instance. On one node everything worked; on two, roughly half would fail — and it would look like
"users are randomly logged out".

**Decision.** One `IHandleStore` over an `auth_handles` table. Redis is the conventional choice, but
for a handful of rows that live sixty seconds it is a second piece of infrastructure to run, secure
and back up. PostgreSQL is already required, and it is transactional.

**Two properties that had to be right.**

- **Atomic redemption.** `DELETE … RETURNING` is a single statement, so of two instances racing on one
  handle exactly one gets the payload. Read-then-delete would let both win.
- **Nothing usable at rest.** Only the SHA-256 hash is stored. These are bearer secrets — a live MFA
  handle read out of a database dump would otherwise be a way past a password.

The `purpose` is part of the lookup, so a passkey challenge cannot be redeemed as an MFA handle.

**Verification.** `tools/multi-instance-smoke.mjs` runs two instances and crosses them deliberately at
every step — start on A, finish on B. **15/15**. This is the one thing a single-instance test cannot
show.

**Still open.** The sign-in rate limiter remains per-process, so N instances allow N× the attempts.
Moving it to the database would mean a write per request; the usual answer is to limit at the load
balancer, or to add Redis. Recorded here rather than left to be discovered.
