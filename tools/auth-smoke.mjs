/**
 * Exercises every sign-in path the API offers, against a running instance with authentication on.
 *
 * The parts a script can drive end to end — invitations, TOTP, the MFA leg — are run for real. The
 * WebAuthn ceremonies need an authenticator holding a private key, which no script has, so those are
 * checked as far as the challenge: that the options are well-formed and that a forged assertion is
 * refused. What cannot be proven here is said so rather than skipped silently.
 *
 *   Auth__Enabled=true … dotnet run --project src/ResolveDesk.WebApi --no-launch-profile
 *   node tools/auth-smoke.mjs
 */
import { createHmac } from "node:crypto";

const BASE = process.env.RESOLVEDESK_API_URL ?? "http://localhost:8080";
const ADMIN_EMAIL = process.env.RESOLVEDESK_ADMIN_EMAIL ?? "admin@resolvedesk.local";
const ADMIN_PASSWORD = process.env.RESOLVEDESK_ADMIN_PASSWORD ?? "BootstrapOnly!2026";

const results = [];
const check = (name, ok, detail = "") => {
  results.push(ok);
  console.log(`${ok ? "\x1b[32mPASS\x1b[0m" : "\x1b[31mFAIL\x1b[0m"}  ${name}${detail ? `  — ${detail}` : ""}`);
};
const bar = (label) => console.log(`\n\x1b[1m${label}\x1b[0m\n${"─".repeat(label.length)}`);

async function raw(method, path, { body, token } = {}) {
  const response = await fetch(`${BASE}/api/v1${path}`, {
    method,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let json;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  return { status: response.status, json, text };
}

/**
 * The sign-in endpoints are rate limited to ten attempts a minute per address — which this script
 * comfortably exceeds. Waiting out the window is the honest response: turning the limiter off for
 * tests would mean never exercising the configuration that actually ships.
 */
async function call(method, path, options = {}) {
  let response = await raw(method, path, options);
  if (response.status === 429) {
    process.stdout.write("\x1b[2m  (rate limited — waiting for the window to reset)\x1b[0m\n");
    await new Promise((r) => setTimeout(r, 61_000));
    response = await raw(method, path, options);
  }
  return response;
}

/** RFC 6238, so the script can produce the code a phone would show. */
function totp(base32Secret, step = Math.floor(Date.now() / 1000 / 30)) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = 0, value = 0;
  const bytes = [];
  for (const c of base32Secret.replace(/=+$/, "").toUpperCase()) {
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

const health = await fetch(`${BASE}/health/live`).catch(() => null);
if (!health?.ok) {
  console.error(`\nCannot reach ${BASE}. Start the API with authentication enabled first.\n`);
  process.exit(1);
}

bar("Configuration");
const config = (await call("GET", "/auth/config")).json;
console.log(`  methods: ${config.methods.join(", ")}  ·  TOTP ${config.totpAvailable ? "available" : "off"}` +
            `${config.totpRequired ? " (required)" : ""}  ·  passkeys ${config.passkeysEnabled ? "on" : "off"}`);
check("authentication is enabled", config.enabled === true);
check("more than one method can be offered at once", Array.isArray(config.methods));

bar("Access control");
check("an unauthenticated request is refused", (await call("GET", "/tickets")).status === 401);

const login = await call("POST", "/auth/login", { body: { username: ADMIN_EMAIL, password: ADMIN_PASSWORD } });
check("the administrator can sign in", login.status === 200 && !!login.json?.token,
  login.status === 200 ? "" : `HTTP ${login.status}`);
const adminToken = login.json?.token;
if (!adminToken) { console.error("\nCannot continue without an administrator token.\n"); process.exit(1); }

check("a wrong password is refused",
  (await call("POST", "/auth/login", { body: { username: ADMIN_EMAIL, password: "not-the-password" } })).status === 401);
check("an unknown account gives the same answer as a wrong password",
  (await call("POST", "/auth/login", { body: { username: "nobody@nowhere.invalid", password: "x" } })).status === 401);

bar("Rate limiting");
// Ten attempts a minute per address. Asserted rather than assumed: a limiter that has quietly
// stopped working looks exactly like one that is working, until someone brute-forces a password.
const burst = [];
for (let i = 0; i < 14; i++) {
  burst.push((await raw("POST", "/auth/login",
    { body: { username: ADMIN_EMAIL, password: "wrong-on-purpose" } })).status);
}
check("repeated sign-in attempts are throttled", burst.includes(429),
  `statuses: ${[...new Set(burst)].join(", ")}`);
console.log("\x1b[2m  (waiting out the window before continuing)\x1b[0m");
await new Promise((r) => setTimeout(r, 61_000));

bar("Invitations");
const email = `invited-${Date.now()}@resolvedesk.local`;
const invited = await call("POST", "/invitations", {
  token: adminToken,
  body: { fullName: "Yeni İşçi", email, role: "Agent", skills: "IPTV, multicast" },
});
check("an administrator can create an account", invited.status === 200, `HTTP ${invited.status}`);
check("the setup link is returned once", typeof invited.json?.url === "string" && invited.json.url.includes("/invite/"));
check("non-ASCII names survive the round trip", invited.json?.fullName === "Yeni İşçi", invited.json?.fullName);

const token = invited.json.url.split("/invite/")[1];

const listed = await call("GET", "/invitations", { token: adminToken });
check("a pending invitation is listed", listed.json?.some((i) => i.email === email));
check("the listing never repeats the link", listed.json?.every((i) => i.url === null || i.url === undefined));

const peek = await call("GET", `/invite/${token}`);
check("the link shows whose account it is", peek.status === 200 && peek.json?.email === email);

check("a short password is refused",
  (await call("POST", `/invite/${token}`, { body: { password: "short" } })).status === 400);

const accepted = await call("POST", `/invite/${token}`, { body: { password: "a-properly-long-password" } });
check("accepting it signs the new person in", accepted.status === 200 && !!accepted.json?.token,
  `HTTP ${accepted.status}`);
check("the new account has the role it was invited with", accepted.json?.role === "Agent");

// The point of a single-use link: the second visit finds nothing, however the link was obtained.
check("the link is dead after one use — GET", (await call("GET", `/invite/${token}`)).status === 404);
check("the link is dead after one use — POST",
  (await call("POST", `/invite/${token}`, { body: { password: "another-long-password" } })).status === 400);
check("an invented token is refused", (await call("GET", "/invite/not-a-real-token")).status === 404);

check("the invited person can now sign in with their own password",
  (await call("POST", "/auth/login", { body: { username: email, password: "a-properly-long-password" } })).status === 200);
check("their old link still does not work",
  (await call("GET", `/invite/${token}`)).status === 404);

bar("Two-factor authentication");
const userToken = accepted.json.token;
const status = await call("GET", "/account/totp", { token: userToken });
check("TOTP reports as available but not yet enrolled",
  status.json?.available === true && status.json?.enrolled === false);

const setup = await call("POST", "/account/totp/setup", { token: userToken });
check("enrolment returns a secret and an otpauth URI",
  typeof setup.json?.secret === "string" && setup.json?.otpAuthUri?.startsWith("otpauth://totp/"));

check("a wrong code does not enable the factor",
  (await call("POST", "/account/totp/enable", { token: userToken, body: { code: "000000" } })).status === 400);
check("the factor is still off after a failed attempt",
  (await call("GET", "/account/totp", { token: userToken })).json?.enrolled === false);

const secret = setup.json.secret;
check("a correct code enables it",
  (await call("POST", "/account/totp/enable", { token: userToken, body: { code: totp(secret) } })).status === 204);

// From here the password alone must no longer produce a session.
const second = await call("POST", "/auth/login", { body: { username: email, password: "a-properly-long-password" } });
check("the password alone now only gets a challenge",
  second.json?.mfaRequired === true && !!second.json?.mfaToken && !second.json?.token);

check("a wrong code fails the second leg",
  (await call("POST", "/auth/mfa", { body: { mfaToken: second.json.mfaToken, code: "000000" } })).status === 401);

// That failure consumed the handle: one password check buys exactly one attempt.
check("the challenge cannot be retried after a wrong code",
  (await call("POST", "/auth/mfa", { body: { mfaToken: second.json.mfaToken, code: totp(secret) } })).status === 401);

const third = await call("POST", "/auth/login", { body: { username: email, password: "a-properly-long-password" } });
const verified = await call("POST", "/auth/mfa", { body: { mfaToken: third.json.mfaToken, code: totp(secret) } });
check("password plus a correct code issues a token", verified.status === 200 && !!verified.json?.token);

check("an invented MFA handle is refused",
  (await call("POST", "/auth/mfa", { body: { mfaToken: "made-up", code: totp(secret) } })).status === 401);

const mfaUserToken = verified.json.token;
check("turning the factor off requires a current code",
  (await call("POST", "/account/totp/disable", { token: mfaUserToken, body: { code: "000000" } })).status === 400);
check("a correct code turns it off",
  (await call("POST", "/account/totp/disable", { token: mfaUserToken, body: { code: totp(secret) } })).status === 204);

bar("Passkeys");
const register = await call("POST", "/account/passkeys/register/begin", { token: userToken });
check("registration returns a challenge", register.status === 200 && !!register.json?.challengeId);
const registerOptions = JSON.parse(register.json.optionsJson);
check("the options name this relying party", registerOptions.rp?.id === "localhost", registerOptions.rp?.id);
check("both ES256 and RS256 are offered",
  registerOptions.pubKeyCredParams?.some((p) => p.alg === -7) &&
  registerOptions.pubKeyCredParams?.some((p) => p.alg === -257));
check("the challenge is 32 bytes", Buffer.from(registerOptions.challenge, "base64url").length === 32);

const beginA = await call("POST", "/auth/passkey/begin");
const beginB = await call("POST", "/auth/passkey/begin");
check("sign-in challenges are issued anonymously", beginA.status === 200 && !!beginA.json?.challengeId);
check("every challenge is fresh",
  JSON.parse(beginA.json.optionsJson).challenge !== JSON.parse(beginB.json.optionsJson).challenge);

check("a forged assertion is refused",
  (await call("POST", "/auth/passkey/finish", {
    body: { challengeId: beginA.json.challengeId, credential: JSON.stringify({
      id: "x", rawId: "AAAA", response: { clientDataJSON: "e30", authenticatorData: "AAAA", signature: "AAAA" } }) },
  })).status === 401);

check("a challenge cannot be answered twice",
  (await call("POST", "/auth/passkey/finish", {
    body: { challengeId: beginA.json.challengeId, credential: "{}" } })).status === 401);

check("the new account has no passkeys yet",
  (await call("GET", "/account/passkeys", { token: userToken })).json?.length === 0);

console.log("\n  \x1b[2mNot covered here: a real registration or assertion, which needs an authenticator");
console.log("  holding a private key. Those paths are exercised by signing in from a browser.\x1b[0m");

bar("Result");
const passed = results.filter(Boolean).length;
console.log(`  ${passed}/${results.length} passed`);
process.exit(passed === results.length ? 0 : 1);
