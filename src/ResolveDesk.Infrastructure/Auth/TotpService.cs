using System.Security.Cryptography;
using System.Text;
using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure.Auth;

/// <summary>
/// Time-based one-time passwords, RFC 6238: HMAC-SHA1 over a 30-second counter, truncated to six
/// digits. Written out rather than taken from a package because the algorithm is thirty lines, and a
/// dependency that pulls in reflection would cost the API its clean AOT build.
///
/// Enrolment is two steps on purpose. The secret is stored the moment it is generated but the factor
/// stays off until a code proves the authenticator actually holds it — otherwise a mistyped scan
/// locks the account owner out of their own account.
/// </summary>
public sealed class TotpService(IAuthStore store, AuthOptions options) : ITotpService
{
    private const int Digits = 6;
    private const int StepSeconds = 30;

    public async Task<(string Secret, string OtpAuthUri)> BeginEnrolmentAsync(long userId, CancellationToken ct = default)
    {
        // 160 bits — the size RFC 4226 recommends for HMAC-SHA1.
        var secret = Base32Encode(RandomNumberGenerator.GetBytes(20));
        await store.SetTotpSecretAsync(userId, secret, enabled: false, ct);

        var issuer = Uri.EscapeDataString(options.Totp.Issuer);
        var account = Uri.EscapeDataString($"user-{userId}");
        var uri = $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}" +
                  $"&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
        return (secret, uri);
    }

    public async Task<bool> ConfirmEnrolmentAsync(long userId, string code, CancellationToken ct = default)
    {
        var secret = await store.GetTotpSecretAsync(userId, ct);
        if (secret is null || !Verify(secret, code)) return false;

        await store.SetTotpSecretAsync(userId, secret, enabled: true, ct);
        return true;
    }

    public async Task<bool> VerifyAsync(long userId, string code, CancellationToken ct = default)
    {
        var secret = await store.GetTotpSecretAsync(userId, ct);
        return secret is not null && await store.IsTotpEnabledAsync(userId, ct) && Verify(secret, code);
    }

    public Task<bool> IsEnabledAsync(long userId, CancellationToken ct = default) =>
        store.IsTotpEnabledAsync(userId, ct);

    public async Task<bool> DisableAsync(long userId, string code, CancellationToken ct = default)
    {
        // A current code is required: a stolen session should not be able to strip the second factor.
        if (!await VerifyAsync(userId, code, ct)) return false;
        await store.SetTotpSecretAsync(userId, null, enabled: false, ct);
        return true;
    }

    private bool Verify(string secret, string code)
    {
        var digits = new string((code ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != Digits) return false;

        byte[] key;
        try { key = Base32Decode(secret); }
        catch (FormatException) { return false; }

        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;
        var window = options.Totp.WindowSteps;

        // Every candidate step is checked, and the comparison is constant-time, so neither the answer
        // nor how long it took reveals which step matched.
        var matched = false;
        for (var offset = -window; offset <= window; offset++)
        {
            var expected = Compute(key, step + offset);
            matched |= CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(digits));
        }
        return matched;
    }

    private static string Compute(byte[] key, long counter)
    {
        Span<byte> buffer = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(buffer, counter);

        Span<byte> mac = stackalloc byte[20];
        HMACSHA1.HashData(key, buffer, mac);

        // Dynamic truncation, RFC 4226 §5.3: the low nibble of the last byte picks the offset.
        var offset = mac[^1] & 0x0F;
        var binary = ((mac[offset] & 0x7F) << 24)
                   | ((mac[offset + 1] & 0xFF) << 16)
                   | ((mac[offset + 2] & 0xFF) << 8)
                   | (mac[offset + 3] & 0xFF);

        return (binary % (int)Math.Pow(10, Digits)).ToString().PadLeft(Digits, '0');
    }

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    internal static string Base32Encode(byte[] data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return builder.ToString();
    }

    internal static byte[] Base32Decode(string value)
    {
        var bytes = new List<byte>(value.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in value.TrimEnd('=').ToUpperInvariant())
        {
            var index = Alphabet.IndexOf(c);
            if (index < 0) throw new FormatException($"'{c}' is not a base32 character.");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. bytes];
    }
}
