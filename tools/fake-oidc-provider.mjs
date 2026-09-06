/**
 * A minimal, standards-shaped OpenID Connect provider, for testing the real one.
 *
 * The alternative was to test the OIDC flow by inspecting the redirect URL and stopping there, which
 * would leave the parts that actually matter — JWKS retrieval, RS256 signature validation, the nonce
 * check, PKCE verification, claim mapping — entirely unexercised. Those are exactly the parts that
 * fail silently when they are wrong.
 *
 * It signs real tokens with a real key, and it *enforces* PKCE rather than ignoring it, so a client
 * that forgets the code verifier fails here the way it would against a genuine provider.
 *
 *   node tools/fake-oidc-provider.mjs            # listens on 9099
 *   PORT=9099 ROLE=admins node tools/fake-oidc-provider.mjs
 */
import { createHash, createSign, generateKeyPairSync } from "node:crypto";
import { createServer } from "node:http";

const PORT = Number(process.env.PORT ?? 9099);
const ISSUER = process.env.ISSUER ?? `http://localhost:${PORT}`;
const EMAIL = process.env.EMAIL ?? "sso-user@resolvedesk.local";
const NAME = process.env.NAME ?? "SSO İstifadəçi";
const ROLE = process.env.ROLE ?? "agents";

const { publicKey, privateKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
const jwk = publicKey.export({ format: "jwk" });
const KID = "test-key-1";

const b64 = (input) => Buffer.from(input).toString("base64url");

function sign(payload) {
  const header = b64(JSON.stringify({ alg: "RS256", typ: "JWT", kid: KID }));
  const body = b64(JSON.stringify(payload));
  const signer = createSign("RSA-SHA256");
  signer.update(`${header}.${body}`);
  return `${header}.${body}.${signer.sign(privateKey, "base64url")}`;
}

/** code -> what the authorization request promised, so /token can hold the client to it. */
const codes = new Map();

const json = (res, body, status = 200) => {
  res.writeHead(status, { "Content-Type": "application/json" });
  res.end(JSON.stringify(body));
};

const server = createServer(async (req, res) => {
  const url = new URL(req.url, ISSUER);

  if (url.pathname === "/.well-known/openid-configuration") {
    return json(res, {
      issuer: ISSUER,
      authorization_endpoint: `${ISSUER}/authorize`,
      token_endpoint: `${ISSUER}/token`,
      jwks_uri: `${ISSUER}/jwks`,
      response_types_supported: ["code"],
      subject_types_supported: ["public"],
      id_token_signing_alg_values_supported: ["RS256"],
      code_challenge_methods_supported: ["S256"],
    });
  }

  // Not part of OpenID Connect. It exists so the smoke test can ask this provider what it is
  // configured to claim, instead of hard-coding defaults that silently drift out of step with it.
  if (url.pathname === "/testing/identity") {
    return json(res, { email: EMAIL, name: NAME, role: ROLE });
  }

  if (url.pathname === "/jwks") {
    return json(res, { keys: [{ ...jwk, kid: KID, use: "sig", alg: "RS256" }] });
  }

  // Stands in for the consent screen: records the request and bounces straight back.
  if (url.pathname === "/authorize") {
    const redirectUri = url.searchParams.get("redirect_uri");
    const state = url.searchParams.get("state");
    if (!redirectUri) return json(res, { error: "invalid_request" }, 400);

    const code = `code-${Math.random().toString(36).slice(2)}`;
    codes.set(code, {
      nonce: url.searchParams.get("nonce"),
      challenge: url.searchParams.get("code_challenge"),
      method: url.searchParams.get("code_challenge_method"),
      clientId: url.searchParams.get("client_id"),
      redirectUri,
    });

    const back = new URL(redirectUri);
    back.searchParams.set("code", code);
    if (state) back.searchParams.set("state", state);
    res.writeHead(302, { Location: back.toString() });
    return res.end();
  }

  if (url.pathname === "/token" && req.method === "POST") {
    const body = await new Promise((resolve) => {
      let raw = "";
      req.on("data", (chunk) => { raw += chunk; });
      req.on("end", () => resolve(new URLSearchParams(raw)));
    });

    const record = codes.get(body.get("code"));
    // Single-use, exactly like a real provider: a replayed code buys nothing.
    codes.delete(body.get("code"));
    if (!record) return json(res, { error: "invalid_grant" }, 400);

    // PKCE is enforced, not merely accepted, so a client that drops the verifier fails here.
    if (record.challenge) {
      const verifier = body.get("code_verifier");
      if (!verifier) return json(res, { error: "invalid_grant", error_description: "code_verifier missing" }, 400);
      const expected = createHash("sha256").update(verifier).digest("base64url");
      if (expected !== record.challenge) {
        return json(res, { error: "invalid_grant", error_description: "code_verifier mismatch" }, 400);
      }
    }
    if (record.redirectUri !== body.get("redirect_uri")) {
      return json(res, { error: "invalid_grant", error_description: "redirect_uri mismatch" }, 400);
    }

    const now = Math.floor(Date.now() / 1000);
    const idToken = sign({
      iss: ISSUER,
      sub: "user-0001",
      aud: record.clientId,
      iat: now,
      exp: now + 300,
      nonce: record.nonce,
      email: EMAIL,
      name: NAME,
      roles: [ROLE],
    });

    return json(res, { id_token: idToken, access_token: "opaque", token_type: "Bearer", expires_in: 300 });
  }

  json(res, { error: "not_found" }, 404);
});

server.listen(PORT, () => {
  console.log(`fake OIDC provider on ${ISSUER}`);
  console.log(`  email ${EMAIL} · role ${ROLE}`);
});
