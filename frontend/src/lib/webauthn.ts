/**
 * The browser half of WebAuthn.
 *
 * The API hands over the options blob exactly as the specification defines it, which is JSON with
 * base64url fields; `navigator.credentials` wants ArrayBuffers. Converting between the two is all
 * this file does — no policy, no decisions, so the security-relevant logic stays server-side where
 * it can be tested.
 */

// Returns an ArrayBuffer rather than a view: `BufferSource` will not accept a Uint8Array whose
// buffer type is merely ArrayBufferLike, which is what the array helpers infer.
const fromBase64Url = (value: string): ArrayBuffer => {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(padded.padEnd(padded.length + ((4 - (padded.length % 4)) % 4), "="));
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
};

const toBase64Url = (buffer: ArrayBuffer): string =>
  btoa(String.fromCharCode(...new Uint8Array(buffer)))
    .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

/** Whether this browser can do WebAuthn at all. Safari on an old iOS cannot, and should be told so. */
export const passkeysSupported = (): boolean =>
  typeof window !== "undefined" &&
  typeof window.PublicKeyCredential === "function" &&
  !!navigator.credentials;

/**
 * Whether a passkey can be created *on this device* — Face ID, Touch ID, Windows Hello. False on a
 * desktop with no biometric hardware, where a security key would still work; the label the UI shows
 * depends on the answer, so it is worth asking rather than assuming.
 */
export async function platformAuthenticatorAvailable(): Promise<boolean> {
  if (!passkeysSupported()) return false;
  try {
    return await window.PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
  } catch {
    return false;
  }
}

interface ServerCreationOptions {
  challenge: string;
  rp: { id: string; name: string };
  user: { id: string; name: string; displayName: string };
  pubKeyCredParams: { type: "public-key"; alg: number }[];
  timeout?: number;
  attestation?: AttestationConveyancePreference;
  authenticatorSelection?: AuthenticatorSelectionCriteria;
}

interface ServerRequestOptions {
  challenge: string;
  rpId: string;
  timeout?: number;
  userVerification?: UserVerificationRequirement;
}

/** Runs the registration ceremony and returns what the API expects to be handed back. */
export async function createPasskey(optionsJson: string): Promise<string> {
  const options = JSON.parse(optionsJson) as ServerCreationOptions;

  const credential = (await navigator.credentials.create({
    publicKey: {
      ...options,
      challenge: fromBase64Url(options.challenge),
      user: { ...options.user, id: fromBase64Url(options.user.id) },
    },
  })) as PublicKeyCredential | null;

  if (!credential) throw new Error("No credential was created.");
  const response = credential.response as AuthenticatorAttestationResponse;

  return JSON.stringify({
    id: credential.id,
    rawId: toBase64Url(credential.rawId),
    response: {
      clientDataJSON: toBase64Url(response.clientDataJSON),
      attestationObject: toBase64Url(response.attestationObject),
    },
  });
}

/** Runs the sign-in ceremony. No username is needed: the authenticator names the account. */
export async function getPasskeyAssertion(optionsJson: string): Promise<string> {
  const options = JSON.parse(optionsJson) as ServerRequestOptions;

  const credential = (await navigator.credentials.get({
    publicKey: { ...options, challenge: fromBase64Url(options.challenge) },
  })) as PublicKeyCredential | null;

  if (!credential) throw new Error("No credential was returned.");
  const response = credential.response as AuthenticatorAssertionResponse;

  return JSON.stringify({
    id: credential.id,
    rawId: toBase64Url(credential.rawId),
    response: {
      clientDataJSON: toBase64Url(response.clientDataJSON),
      authenticatorData: toBase64Url(response.authenticatorData),
      signature: toBase64Url(response.signature),
      userHandle: response.userHandle ? toBase64Url(response.userHandle) : null,
    },
  });
}

/**
 * Cancelling the system prompt throws, and so does a genuine failure. Telling them apart matters:
 * "you cancelled" is not an error worth showing in red.
 */
export const wasCancelled = (error: unknown): boolean =>
  error instanceof DOMException && (error.name === "NotAllowedError" || error.name === "AbortError");
