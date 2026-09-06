using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ResolveDesk.Infrastructure.Auth;

/// <summary>What the browser sends back from navigator.credentials.create().</summary>
public sealed record RegistrationCredential(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("rawId")] string RawId,
    [property: JsonPropertyName("response")] RegistrationResponse Response);

public sealed record RegistrationResponse(
    [property: JsonPropertyName("clientDataJSON")] string ClientDataJson,
    [property: JsonPropertyName("attestationObject")] string AttestationObject);

/// <summary>What the browser sends back from navigator.credentials.get().</summary>
public sealed record AssertionCredential(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("rawId")] string RawId,
    [property: JsonPropertyName("response")] AssertionResponse Response);

public sealed record AssertionResponse(
    [property: JsonPropertyName("clientDataJSON")] string ClientDataJson,
    [property: JsonPropertyName("authenticatorData")] string AuthenticatorData,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("userHandle")] string? UserHandle);

/// <summary>The parts of clientDataJSON that have to be checked.</summary>
public sealed record ClientData(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("challenge")] string Challenge,
    [property: JsonPropertyName("origin")] string Origin);

/// <summary>
/// A challenge in flight, as stored. The challenge itself is base64url rather than bytes because it
/// travels through a text column; the user id is null for a sign-in, where the credential names the
/// account rather than the request.
/// </summary>
public sealed record PendingChallenge(
    [property: JsonPropertyName("challenge")] string Challenge,
    [property: JsonPropertyName("userId")] long? UserId);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PendingChallenge))]
[JsonSerializable(typeof(RegistrationCredential))]
[JsonSerializable(typeof(AssertionCredential))]
[JsonSerializable(typeof(ClientData))]
internal sealed partial class WebAuthnJsonContext : JsonSerializerContext;

/// <summary>
/// The cryptography behind WebAuthn, kept apart from the service that stores things.
///
/// Attestation is deliberately not verified. Attestation answers "what make and model of
/// authenticator is this", which matters when an organisation must allow only certified hardware; for
/// signing into a support desk it buys nothing and costs a metadata service and a certificate chain.
/// The parts that actually carry the security guarantee — the challenge, the origin, the relying
/// party, and the signature over both — are all checked.
/// </summary>
internal static class WebAuthn
{
    /// <summary>Authenticator data flags, WebAuthn §6.1.</summary>
    private const byte UserPresent = 0x01;
    private const byte UserVerified = 0x04;
    private const byte AttestedCredentialData = 0x40;

    public static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }

    public static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Pulls the credential id and its public key out of an attestation object. The COSE key is kept
    /// as-is rather than converted: it is self-describing, and re-encoding is a chance to lose a curve.
    /// </summary>
    public static (byte[] CredentialId, byte[] CosePublicKey, long SignCount) ReadRegistration(
        byte[] attestationObject, string relyingPartyId, bool requireUserVerification)
    {
        var reader = new CborReader(attestationObject);
        var entries = reader.ReadStartMap() ?? throw new FormatException("Attestation object is not a map.");

        byte[]? authData = null;
        for (var i = 0; i < entries; i++)
        {
            var key = reader.ReadTextString();
            if (key == "authData") authData = reader.ReadByteString();
            else reader.SkipValue();
        }
        reader.ReadEndMap();

        if (authData is null) throw new FormatException("Attestation object has no authenticator data.");
        return ReadAuthData(authData, relyingPartyId, requireUserVerification);
    }

    /// <summary>
    /// Authenticator data layout (WebAuthn §6.1):
    /// 32 bytes RP ID hash · 1 byte flags · 4 bytes counter · then, when the attested-credential flag
    /// is set, 16 bytes AAGUID · 2 bytes id length · the credential id · the COSE public key.
    /// </summary>
    private static (byte[] CredentialId, byte[] CosePublicKey, long SignCount) ReadAuthData(
        byte[] authData, string relyingPartyId, bool requireUserVerification)
    {
        if (authData.Length < 37) throw new FormatException("Authenticator data is too short.");

        var expectedRpIdHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(relyingPartyId));
        if (!CryptographicOperations.FixedTimeEquals(authData.AsSpan(0, 32), expectedRpIdHash))
            throw new CryptographicException("The credential was created for a different relying party.");

        var flags = authData[32];
        if ((flags & UserPresent) == 0)
            throw new CryptographicException("The authenticator did not report user presence.");
        if (requireUserVerification && (flags & UserVerified) == 0)
            throw new CryptographicException("The authenticator did not verify the user.");
        if ((flags & AttestedCredentialData) == 0)
            throw new FormatException("The authenticator data carries no credential.");

        var signCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(authData.AsSpan(33, 4));

        var offset = 37 + 16;                                     // skip the AAGUID
        var idLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(authData.AsSpan(offset, 2));
        offset += 2;

        var credentialId = authData.AsSpan(offset, idLength).ToArray();
        offset += idLength;

        // The COSE key runs to the end of whatever this reader consumes — extensions may follow it.
        var keyReader = new CborReader(authData.AsMemory(offset).ToArray());
        var keyBytes = keyReader.ReadEncodedValue().ToArray();

        return (credentialId, keyBytes, signCount);
    }

    /// <summary>
    /// Verifies an assertion signature over authenticatorData ‖ SHA-256(clientDataJSON), which is what
    /// binds the signature to this origin, this challenge and this relying party at once.
    /// </summary>
    public static bool VerifyAssertion(
        byte[] cosePublicKey, byte[] authenticatorData, byte[] clientDataJson, byte[] signature,
        string relyingPartyId, bool requireUserVerification, out long signCount)
    {
        signCount = 0;
        if (authenticatorData.Length < 37) return false;

        var expectedRpIdHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(relyingPartyId));
        if (!CryptographicOperations.FixedTimeEquals(authenticatorData.AsSpan(0, 32), expectedRpIdHash))
            return false;

        var flags = authenticatorData[32];
        if ((flags & UserPresent) == 0) return false;
        if (requireUserVerification && (flags & UserVerified) == 0) return false;

        signCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(authenticatorData.AsSpan(33, 4));

        var payload = new byte[authenticatorData.Length + 32];
        authenticatorData.CopyTo(payload, 0);
        SHA256.HashData(clientDataJson).CopyTo(payload, authenticatorData.Length);

        return VerifySignature(cosePublicKey, payload, signature);
    }

    /// <summary>
    /// COSE key labels (RFC 8152): 1 = key type, 3 = algorithm, −1 = curve or RSA modulus,
    /// −2 = x or RSA exponent, −3 = y. Only ES256 and RS256 are accepted — between them they cover
    /// every platform authenticator in circulation, and each extra algorithm is more parsing to get
    /// wrong for no practical gain.
    /// </summary>
    private static bool VerifySignature(byte[] cosePublicKey, byte[] payload, byte[] signature)
    {
        var reader = new CborReader(cosePublicKey);
        var entries = reader.ReadStartMap() ?? throw new FormatException("COSE key is not a map.");

        int keyType = 0, algorithm = 0, curve = 0;
        byte[]? x = null, y = null, modulus = null, exponent = null;

        for (var i = 0; i < entries; i++)
        {
            var label = reader.ReadInt32();
            switch (label)
            {
                case 1: keyType = reader.ReadInt32(); break;
                case 3: algorithm = reader.ReadInt32(); break;
                case -1:
                    // For EC2 this is the curve id; for RSA it is the modulus.
                    if (reader.PeekState() is CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger)
                        curve = reader.ReadInt32();
                    else modulus = reader.ReadByteString();
                    break;
                case -2:
                    if (keyType == 3) exponent = reader.ReadByteString();
                    else x = reader.ReadByteString();
                    break;
                case -3: y = reader.ReadByteString(); break;
                default: reader.SkipValue(); break;
            }
        }
        reader.ReadEndMap();

        // ES256 over P-256. The signature arrives DER-encoded, which is what the Rfc3279 format means.
        if (keyType == 2 && algorithm == -7 && curve == 1 && x is not null && y is not null)
        {
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y },
            });
            return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }

        // RS256 — Windows Hello with a TPM commonly produces these.
        if (keyType == 3 && algorithm == -257 && modulus is not null && exponent is not null)
        {
            using var rsa = RSA.Create(new RSAParameters { Modulus = modulus, Exponent = exponent });
            return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        return false;
    }
}
