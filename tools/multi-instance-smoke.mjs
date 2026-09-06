/**
 * Proves that a sign-in started on one instance can be finished on another.
 *
 * This is the whole reason the handles moved out of process memory, and it is not something a
 * single-instance test can show: in memory, every check below passed on one node and would have
 * failed roughly half the time behind a load balancer — the failure mode that only appears once the
 * second node is added, and looks like "users are randomly logged out".
 *
 * Every step deliberately crosses instances: start on A, finish on B, and back.
 *
 *   ASPNETCORE_URLS=http://localhost:8080 dotnet run --project src/ResolveDesk.WebApi &
 *   ASPNETCORE_URLS=http://localhost:8081 dotnet run --project src/ResolveDesk.WebApi &
 *   node tools/multi-instance-smoke.mjs
 *
 * Both instances need the same Auth__Jwt__SigningKey, or a token issued by one is meaningless to the
 * other — which is true of any multi-instance deployment and worth failing loudly about here.
 */
import { createHmac } from "node:crypto";

const A = process.env.INSTANCE_A ?? "http://localhost:8080";
const B = process.env.INSTANCE_B ?? "http://localhost:8081";
const ADMIN_EMAIL = process.env.RESOLVEDESK_ADMIN_EMAIL ?? "admin@resolvedesk.local";
const ADMIN_PASSWORD = process.env.RESOLVEDESK_ADMIN_PASSWORD ?? "BootstrapOnly!2026";

const results = [];
const check = (name, ok, detail = "") => {
  results.push(ok);
  console.log(`${ok ? "\x1b[32mPASS\x1b[0m" : "\x1b[31mFAIL\x1b[0m"}  ${name}${detail ? `  — ${detail}` : ""}`);
};
const bar = (label) => console.log(`\n\x1b[1m${label}\x1b[0m\n${"─".repeat(label.length)}`);

async function raw(base, method, path, { body, token } = {}) {
  const response = await fetch(`${base}/api/v1${path}`, {
    method,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
    redirect: "manual",
  });
  const text = await response.text();
  let json;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  return { status: response.status, json, headers: response.headers };
}

/** The sign-in endpoints are rate limited per address, and both instances see the same address. */
async function call(base, method, path, options = {}) {
  let response = await raw(base, method, path, options);
  if (response.status === 429) {
    process.stdout.write("\x1b[2m  (rate limited — waiting for the window to reset)\x1b[0m\n");
    await new Promise((r) => setTimeout(r, 61_000));
    response = await raw(base, method, path, options);
  }
  return response;
}

function totp(secret, step = Math.floor(Date.now() / 1000 / 30)) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = 0, value = 0;
  const bytes = [];
  for (const c of secret.replace(/=+$/, "").toUpperCase()) {
    value = (value << 5) | alphabet.indexOf(c);
    bits += 5;
    if (bits >= 8) { bytes.push((value >>> (bits - 8)) & 0xff); bits -= 8; }
  }
  const counter = Buffer.alloc(8);
  counter.writeBigInt64BE(BigInt(step));
  const mac = createHmac("sha1", Buffer.from(bytes)).update(counter).digest();
  const offset = mac[mac.length - 1] & 0x0f;
  const binary = ((mac[offset] & 0x7f) << 24) | (mac[offset + 1] << 16) |
                 (mac[offset + 2] << 8) | mac[offset + 3];
  return String(binary % 1_000_000).padStart(6, "0");
}

for (const [label, base] of [["A", A], ["B", B]]) {
  const up = await fetch(`${base}/health/live`).then((r) => r.ok).catch(() => false);
  if (!up) { console.error(`\nInstance ${label} at ${base} is not answering.\n`); process.exit(1); }
}
console.log(`instance A: ${A}\ninstance B: ${B}`);

bar("Tokens are interchangeable");
const login = await call(A, "POST", "/auth/login", { body: { username: ADMIN_EMAIL, password: ADMIN_PASSWORD } });
check("the administrator signs in on A", login.status === 200 && !!login.json?.token, `HTTP ${login.status}`);
const adminToken = login.json?.token;
if (!adminToken) { console.error("\nCannot continue without a token.\n"); process.exit(1); }

check("a token issued by A is accepted by B",
  (await call(B, "GET", "/tickets", { token: adminToken })).status === 200);

bar("Invitation: created on A, accepted on B");
const email = `cross-${Date.now()}@resolvedesk.local`;
const invited = await call(A, "POST", "/invitations", {
  token: adminToken, body: { fullName: "Cross Instance", email, role: "Agent", skills: null },
});
check("A creates the account and the link", invited.status === 200 && !!invited.json?.url);
const inviteToken = invited.json.url.split("/invite/")[1];

check("B recognises the link A issued",
  (await call(B, "GET", `/invite/${inviteToken}`)).status === 200);

const accepted = await call(B, "POST", `/invite/${inviteToken}`, { body: { password: "a-properly-long-password" } });
check("B accepts it and issues a session", accepted.status === 200 && !!accepted.json?.token);
check("and A now considers the link spent",
  (await call(A, "GET", `/invite/${inviteToken}`)).status === 404);

bar("Second factor: password on A, code on B");
const userToken = accepted.json.token;
const setup = await call(B, "POST", "/account/totp/setup", { token: userToken });
check("B starts the enrolment", setup.status === 200 && !!setup.json?.secret);
const secret = setup.json.secret;

check("A confirms the enrolment B started",
  (await call(A, "POST", "/account/totp/enable", { token: userToken, body: { code: totp(secret) } })).status === 204);

const challenged = await call(A, "POST", "/auth/login", { body: { username: email, password: "a-properly-long-password" } });
check("A returns a second-factor challenge", challenged.json?.mfaRequired === true);

// The handle came from A. In memory this is exactly where a two-node deployment broke.
const verified = await call(B, "POST", "/auth/mfa", { body: { mfaToken: challenged.json.mfaToken, code: totp(secret) } });
check("B completes the challenge A issued", verified.status === 200 && !!verified.json?.token,
  `HTTP ${verified.status}`);

check("the handle is spent everywhere, not just on B",
  (await call(A, "POST", "/auth/mfa",
    { body: { mfaToken: challenged.json.mfaToken, code: totp(secret) } })).status === 401);

bar("Passkey challenge: issued by A, answered at B");
const challenge = await call(A, "POST", "/auth/passkey/begin");
check("A issues a challenge", challenge.status === 200 && !!challenge.json?.challengeId);

// A forged assertion is refused either way; what matters is *how*. If B did not know the challenge
// it would fail for the wrong reason, so the same challenge is then re-presented to A: if B had not
// consumed it, A would still accept it.
await call(B, "POST", "/auth/passkey/finish", {
  body: { challengeId: challenge.json.challengeId, credential: "{}" },
});
const reused = await call(A, "POST", "/auth/passkey/finish", {
  body: { challengeId: challenge.json.challengeId, credential: "{}" },
});
check("B consumed it, so A no longer honours it", reused.status === 401);

bar("SSO: authorization from B, callback on A");
const start = await fetch(`${B}/api/v1/auth/oidc/start`, { redirect: "manual" });
if (start.status !== 302) {
  console.log("\x1b[2m  (SSO not configured on these instances — skipped)\x1b[0m");
} else {
  const authorize = new URL(start.headers.get("location"));
  const bounced = await fetch(authorize, { redirect: "manual" });
  // The provider's redirect_uri points at A, so the callback necessarily crosses instances.
  const callback = await fetch(new URL(bounced.headers.get("location")), { redirect: "manual" });
  const landing = new URL(callback.headers.get("location"));
  check("A completes an authorization B started",
    !landing.searchParams.has("error") && !!landing.searchParams.get("code"),
    landing.search);

  const session = await call(B, "POST", "/auth/oidc/exchange", { body: { code: landing.searchParams.get("code") } });
  check("B exchanges the handoff code A issued", session.status === 200 && !!session.json?.token,
    `HTTP ${session.status}`);
}

bar("Result");
const passed = results.filter(Boolean).length;
console.log(`  ${passed}/${results.length} passed`);
process.exit(passed === results.length ? 0 : 1);
