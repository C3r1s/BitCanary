using NSec.Cryptography;

namespace Messenger.Tests.Fakes;

/// <summary>
/// Per D-01: runtime Ed25519 key generation via NSec — no static byte[] constants.
/// Each call produces a fresh, always-valid key pair for the installed NSec version.
/// </summary>
public static class TestKeys
{
    /// <summary>
    /// Returns (publicKey: 32 bytes, privateKey: 32-byte seed).
    /// </summary>
    public static (byte[] PublicKey, byte[] PrivateKey) GenerateEd25519Pair()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });

        var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var privateKey = key.Export(KeyBlobFormat.RawPrivateKey);
        return (publicKey, privateKey);
    }

    /// <summary>
    /// Returns (ikPublic, spkPublic, spkSignature) — a well-formed SPK bundle that
    /// NSecSpkValidator.Validate(...) will accept. Flip a single byte in spkSignature
    /// in tests to produce a rejection case.
    /// </summary>
    public static (byte[] IkPublic, byte[] SpkPublic, byte[] SpkSignature) GenerateSignedSpkBundle()
    {
        using var ikKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });

        var spkPublic = new byte[32];
        Random.Shared.NextBytes(spkPublic);

        var spkSignature = SignatureAlgorithm.Ed25519.Sign(ikKey, spkPublic);
        var ikPublic = ikKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        return (ikPublic, spkPublic, spkSignature);
    }
}
