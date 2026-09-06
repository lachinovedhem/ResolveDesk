/**
 * Drives the whole interactive OIDC flow against tools/fake-oidc-provider.mjs.
 *
 * Everything a browser would do is done here by hand: follow the redirect to the provider, follow the
 * redirect back, take the one-time code out of the URL, exchange it. That is what makes this worth
 * running — the signature, the JWKS lookup, the nonce and the PKCE verifier are all checked by the
 * real code path, not asserted about.
 *
 *   node tools/fake-oidc-provider.mjs &
 *   Auth__Enabled=true Auth__Methods__1=Oidc Auth__Oidc__Authority=http://localhost:9099 \
 *     Auth__Oidc__ClientId=resolvedesk … dotnet run --project src/ResolveDesk.WebApi
 *   node tools/oidc-smoke.mjs
 */
const API = process.env.RESOLVEDESK_API_URL ?? "http://localhost:8080";
const IDP = process.env.OIDC_ISSUER ?? "http://localhost:9099";

const results = [];
const check = (name, ok, detail = "") => {
  results.push(ok);
  console.log(`${ok ? "\x1b[32mPASS\x1b[0m" : "\x1b[31mFAIL\x1b[0m"}  ${name}${detail ? `  — ${detail}` : ""}`);
};
const bar = (label) => console.log(`\n\x1b[1m${label}\x1b[0m\n${"─".repeat(label.length)}`);

const noRedirect = { redirect: "manual" };

/**
 * /auth/oidc/exchange shares the sign-in rate limiter — ten a minute per address — and a developer
 * re-running this script hits it across runs, not within one. Waiting is the honest response; the
 * limiter is a feature, and a test that needed it switched off would never notice it breaking.
 */
async function exchange(code) {
  const send = () => fetch(`${API}/api/v1/auth/oidc/exchange`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code }),
  });
  let response = await send();
  if (response.status === 429) {
    process.stdout.write("\x1b[2m  (rate limited — waiting for the window to reset)\x1b[0m\n");
    await new Promise((r) => setTimeout(r, 61_000));
    response = await send();
  }
  return response;
}

for (const [label, url] of [["API", `${API}/health/live`], ["provider", `${IDP}/jwks`]]) {
  const reachable = await fetch(url).then((r) => r.ok).catch(() => false);
  if (!reachable) {
    console.error(`\nCannot reach the ${label} at ${url}.\n`);
    process.exit(1);
  }
}

// Ask the provider what it will claim, rather than assuming the defaults this script was written
// against. Guessing produced three confusing failures the first time the provider was restarted with
// different settings, on a flow that was working perfectly.
const identity = await (await fetch(`${IDP}/testing/identity`)).json();

bar("Advertised configuration");
const config = await (await fetch(`${API}/api/v1/auth/config`)).json();
check("OIDC is among the enabled methods", config.methods.includes("Oidc"), config.methods.join(", "));
check("the API reports that it runs the flow itself", config.oidcInteractive === true);

bar("Authorization request");
const start = await fetch(`${API}/api/v1/auth/oidc/start`, noRedirect);
check("/start redirects", start.status === 302, `HTTP ${start.status}`);

const authorizeUrl = new URL(start.headers.get("location"));
check("it points at the provider's authorization endpoint",
  authorizeUrl.origin === new URL(IDP).origin && authorizeUrl.pathname === "/authorize");
check("response_type is code", authorizeUrl.searchParams.get("response_type") === "code");
check("PKCE is used with S256", authorizeUrl.searchParams.get("code_challenge_method") === "S256"
  && (authorizeUrl.searchParams.get("code_challenge") ?? "").length >= 43);
check("a state is present", (authorizeUrl.searchParams.get("state") ?? "").length >= 40);
check("a nonce is present", (authorizeUrl.searchParams.get("nonce") ?? "").length >= 40);
check("the openid scope is requested",
  (authorizeUrl.searchParams.get("scope") ?? "").split(" ").includes("openid"));

// Two starts must not share a state, or one sign-in could be completed with another's callback.
const second = await fetch(`${API}/api/v1/auth/oidc/start`, noRedirect);
check("every authorization request is distinct",
  new URL(second.headers.get("location")).searchParams.get("state") !== authorizeUrl.searchParams.get("state"));

bar("Callback");
// The provider bounces straight back, exactly as it would after a consent screen.
const bounced = await fetch(authorizeUrl, noRedirect);
const callbackUrl = new URL(bounced.headers.get("location"));
check("the provider returns a code and the state it was given",
  !!callbackUrl.searchParams.get("code") &&
  callbackUrl.searchParams.get("state") === authorizeUrl.searchParams.get("state"));

const callback = await fetch(callbackUrl, noRedirect);
check("the callback redirects to the app", callback.status === 302, `HTTP ${callback.status}`);

const landing = new URL(callback.headers.get("location"));
check("no token travels in the URL",
  !landing.searchParams.has("token") && !landing.hash.includes("token"),
  landing.search);
check("a one-time handoff code does", (landing.searchParams.get("code") ?? "").length >= 40);

bar("Exchange");
const handoff = landing.searchParams.get("code");
const exchanged = await exchange(handoff);
const session = await exchanged.json();
check("the code buys a session", exchanged.status === 200 && !!session.token, `HTTP ${exchanged.status}`);
check("the account came from the provider's claims", session.email === identity.email, session.email);
check("non-ASCII names survive", session.fullName === identity.name, session.fullName);

// Role mapping is the part an operator gets wrong quietly: everyone lands as an Agent and nobody
// notices until a coordinator cannot assign a ticket. ROLE=admins on the provider proves the claim
// is actually read, rather than everyone defaulting to the same role.
const expectedRole = { admins: "Admin", coordinators: "Coordinator" }[identity.role] ?? "Agent";
check("the provider's role claim decides the local role",
  session.role === expectedRole, `${identity.role} -> ${session.role}`);

check("the session works against a protected endpoint",
  (await fetch(`${API}/api/v1/tickets`, { headers: { Authorization: `Bearer ${session.token}` } })).status === 200);

check("the handoff code is single-use", (await exchange(handoff)).status === 401);
check("an invented handoff code is refused", (await exchange("not-a-real-code")).status === 401);

bar("Replay and tampering");
// The state was consumed by the first callback, so the same callback URL is now worthless.
const replayed = await fetch(callbackUrl, noRedirect);
check("the callback cannot be replayed",
  new URL(replayed.headers.get("location")).searchParams.get("error") === "rejected",
  new URL(replayed.headers.get("location")).search);

const forged = new URL(callbackUrl);
forged.searchParams.set("state", "a-state-nobody-issued");
check("a state the API never issued is rejected",
  new URL((await fetch(forged, noRedirect)).headers.get("location")).searchParams.get("error") === "rejected");

const denied = new URL(`${API}/api/v1/auth/oidc/callback`);
denied.searchParams.set("error", "access_denied");
check("a refusal at the provider is passed through as a reason",
  new URL((await fetch(denied, noRedirect)).headers.get("location")).searchParams.get("error") === "denied");

const bare = await fetch(`${API}/api/v1/auth/oidc/callback`, noRedirect);
check("a callback with nothing in it is rejected",
  new URL(bare.headers.get("location")).searchParams.get("error") === "invalid");

bar("Result");
const passed = results.filter(Boolean).length;
console.log(`  ${passed}/${results.length} passed`);
process.exit(passed === results.length ? 0 : 1);
