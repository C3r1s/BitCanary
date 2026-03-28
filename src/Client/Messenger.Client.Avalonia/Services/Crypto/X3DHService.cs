using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Implements X3DH (Extended Triple Diffie-Hellman) key agreement using NSec.Cryptography
/// (libsodium wrapper). Ed25519 is used for identity keys (signing + identity verification);
/// X25519 is used for DH operations. Ed25519 keys are converted to X25519 at DH time via
/// libsodium P/Invoke.
/// </summary>
public sealed class X3DHService : IX3DHService
{
    // HKDF constants (X3DH spec)
    private static readonly byte[] HkdfSalt = new byte[32]; // 32 zero bytes
    private static readonly byte[] HkdfInfo = "X3DH"u8.ToArray();

    // libsodium P/Invoke for Ed25519 <-> X25519 conversion
    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl)]
    private static extern int crypto_sign_ed25519_pk_to_curve25519(byte[] x25519_pk, byte[] ed25519_pk);

    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl)]
    private static extern int crypto_sign_ed25519_sk_to_curve25519(byte[] x25519_sk, byte[] ed25519_sk);

    /// <summary>
    /// Generates a complete key bundle: IK (Ed25519 for signing), SPK (X25519 for DH),
    /// SPK signature (Ed25519 over SpkPublic using IK).
    /// </summary>
    public X3DHKeyBundle GenerateKeyBundle()
    {
        // IK: Ed25519 (identity + signing)
        using var ikKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var ikPublic = ikKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var ikPrivate = ikKey.Export(KeyBlobFormat.RawPrivateKey); // 32-byte seed

        // SPK: X25519 (for DH)
        using var spkKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var spkPublic = spkKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var spkPrivate = spkKey.Export(KeyBlobFormat.RawPrivateKey);

        // Sign SPK public key with IK (Ed25519)
        var spkSignature = SignatureAlgorithm.Ed25519.Sign(ikKey, spkPublic);

        return new X3DHKeyBundle(
            ikPublic,
            ikPrivate,
            spkPublic,
            spkPrivate,
            spkSignature,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Generates <paramref name="count"/> X25519 one-time pre-key pairs.
    /// </summary>
    public List<OtpKeyPair> GenerateOneTimePreKeys(int count)
    {
        var result = new List<OtpKeyPair>(count);
        for (int i = 0; i < count; i++)
        {
            using var key = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
            {
                ExportPolicy = KeyExportPolicies.AllowPlaintextExport
            });
            result.Add(new OtpKeyPair(
                key.PublicKey.Export(KeyBlobFormat.RawPublicKey),
                key.Export(KeyBlobFormat.RawPrivateKey)));
        }
        return result;
    }

    /// <summary>
    /// Alice's side of X3DH. Validates Bob's SPK signature, generates ephemeral key,
    /// performs 3 or 4 DH operations, derives 32-byte SK via HKDF-SHA-256.
    /// Returns SK and X3dhHeader for transport in MetadataJson.
    /// </summary>
    public (byte[] SharedSecret, X3dhHeader Header) InitiateSession(
        X3DHKeyBundle localBundle,
        byte[] remoteIkPublic,
        byte[] remoteSpkPublic,
        byte[] remoteSpkSignature,
        byte[]? remoteOpkPublic,
        Guid? remoteOpkId)
    {
        // Validate SPK signature against remote IK (Ed25519)
        var remoteIkPub = PublicKey.Import(SignatureAlgorithm.Ed25519, remoteIkPublic, KeyBlobFormat.RawPublicKey);
        if (!SignatureAlgorithm.Ed25519.Verify(remoteIkPub, remoteSpkPublic, remoteSpkSignature))
            throw new CryptographicException("Invalid SPK signature");

        // Generate ephemeral key pair (X25519)
        using var ekKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var ekPublic = ekKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var ekPrivate = ekKey.Export(KeyBlobFormat.RawPrivateKey);

        // Import remote SPK as X25519 public key
        var remoteSpkPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteSpkPublic, KeyBlobFormat.RawPublicKey);

        // Convert local IK (Ed25519 seed) to X25519 private key for DH
        var localIkX25519Private = ConvertEd25519PrivateToX25519(localBundle.IkPrivate, localBundle.IkPublic);

        // Convert remote IK (Ed25519 public) to X25519 public key for DH
        var remoteIkX25519Public = ConvertEd25519PublicToX25519(remoteIkPublic);

        // Perform DH operations
        // DH1 = DH(IK_A_x25519, SPK_B)
        var dh1 = PerformDH(localIkX25519Private, remoteSpkPub);

        // DH2 = DH(EK_A, IK_B_x25519)
        var remoteIkX25519Pub = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteIkX25519Public, KeyBlobFormat.RawPublicKey);
        var dh2 = PerformDH(ekPrivate, remoteIkX25519Pub);

        // DH3 = DH(EK_A, SPK_B)
        var dh3 = PerformDH(ekPrivate, remoteSpkPub);

        // IKM = DH1 || DH2 || DH3 [|| DH4 if OPK present]
        byte[] ikm;
        if (remoteOpkPublic != null)
        {
            var remoteOpkPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteOpkPublic, KeyBlobFormat.RawPublicKey);
            var dh4 = PerformDH(ekPrivate, remoteOpkPub);
            ikm = Concat(dh1, dh2, dh3, dh4);
            CryptographicOperations.ZeroMemory(dh4);
        }
        else
        {
            ikm = Concat(dh1, dh2, dh3);
        }

        // Derive SK via HKDF-SHA-256
        var sk = DeriveKey(ikm);

        // Zero intermediate key material
        CryptographicOperations.ZeroMemory(dh1);
        CryptographicOperations.ZeroMemory(dh2);
        CryptographicOperations.ZeroMemory(dh3);
        CryptographicOperations.ZeroMemory(ikm);
        CryptographicOperations.ZeroMemory(localIkX25519Private);

        var header = new X3dhHeader(ekPublic, remoteOpkId, localBundle.IkPublic);
        return (sk, header);
    }

    /// <summary>
    /// Bob's side of X3DH. Uses his private keys and Alice's public keys from the header
    /// to derive the same SK.
    /// </summary>
    public byte[] RespondToSession(
        X3DHKeyBundle localBundle,
        byte[]? localOpkPrivate,
        X3dhHeader incomingHeader)
    {
        var ekPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, incomingHeader.EkPub, KeyBlobFormat.RawPublicKey);
        var remoteSpkPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, localBundle.SpkPublic, KeyBlobFormat.RawPublicKey);

        // Convert local IK (Ed25519 seed) to X25519 for DH
        var localIkX25519Private = ConvertEd25519PrivateToX25519(localBundle.IkPrivate, localBundle.IkPublic);

        // Convert remote (Alice's) IK (Ed25519 public) to X25519 for DH
        var remoteIkX25519Public = ConvertEd25519PublicToX25519(incomingHeader.IkPub);
        var remoteIkX25519Pub = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteIkX25519Public, KeyBlobFormat.RawPublicKey);

        // DH1 = DH(SPK_B, IK_A_x25519)
        var dh1 = PerformDH(localBundle.SpkPrivate, remoteIkX25519Pub);

        // DH2 = DH(IK_B_x25519, EK_A)
        var dh2 = PerformDH(localIkX25519Private, ekPub);

        // DH3 = DH(SPK_B, EK_A)
        var dh3 = PerformDH(localBundle.SpkPrivate, ekPub);

        byte[] ikm;
        if (localOpkPrivate != null)
        {
            // DH4 = DH(OPK_B, EK_A)
            var dh4 = PerformDH(localOpkPrivate, ekPub);
            ikm = Concat(dh1, dh2, dh3, dh4);
            CryptographicOperations.ZeroMemory(dh4);
        }
        else
        {
            ikm = Concat(dh1, dh2, dh3);
        }

        var sk = DeriveKey(ikm);

        CryptographicOperations.ZeroMemory(dh1);
        CryptographicOperations.ZeroMemory(dh2);
        CryptographicOperations.ZeroMemory(dh3);
        CryptographicOperations.ZeroMemory(ikm);
        CryptographicOperations.ZeroMemory(localIkX25519Private);

        return sk;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts an Ed25519 public key (32 bytes) to its X25519 (Curve25519) equivalent
    /// using libsodium's crypto_sign_ed25519_pk_to_curve25519.
    /// </summary>
    private static byte[] ConvertEd25519PublicToX25519(byte[] ed25519Pk)
    {
        var x25519Pk = new byte[32];
        int result = crypto_sign_ed25519_pk_to_curve25519(x25519Pk, ed25519Pk);
        if (result != 0)
            throw new CryptographicException("Ed25519 public key to X25519 conversion failed");
        return x25519Pk;
    }

    /// <summary>
    /// Converts an Ed25519 private key (32-byte seed) to its X25519 equivalent.
    /// libsodium expects the 64-byte expanded form (seed || public), so we reconstruct it.
    /// </summary>
    private static byte[] ConvertEd25519PrivateToX25519(byte[] ed25519Seed, byte[] ed25519Public)
    {
        // Construct 64-byte expanded secret key: seed (32) || public (32)
        var expandedSk = new byte[64];
        ed25519Seed.CopyTo(expandedSk, 0);
        ed25519Public.CopyTo(expandedSk, 32);

        var x25519Sk = new byte[32];
        int result = crypto_sign_ed25519_sk_to_curve25519(x25519Sk, expandedSk);
        CryptographicOperations.ZeroMemory(expandedSk);
        if (result != 0)
            throw new CryptographicException("Ed25519 private key to X25519 conversion failed");
        return x25519Sk;
    }

    /// <summary>
    /// Performs X25519 DH agreement from a raw 32-byte private key and an imported public key.
    /// Returns the 32-byte raw X25519 output (extracted via HKDF with empty salt/info).
    /// </summary>
    private static byte[] PerformDH(byte[] privateKeyBytes, PublicKey publicKey)
    {
        using var privateKey = Key.Import(KeyAgreementAlgorithm.X25519, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
        using var sharedSecret = KeyAgreementAlgorithm.X25519.Agree(privateKey, publicKey)
            ?? throw new CryptographicException("DH agreement failed");

        // Extract raw 32-byte DH output using HKDF-SHA-256 with no salt/info.
        // NSec's KeyDerivationAlgorithm2.HkdfSha256 accepts SharedSecret directly.
        return KeyDerivationAlgorithm2.HkdfSha256.DeriveBytes(sharedSecret, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, 32);
    }

    /// <summary>
    /// Derives a 32-byte session key from IKM using HKDF-SHA-256 with the X3DH salt and info.
    /// Uses .NET's built-in HKDF since NSec's HKDF requires a SharedSecret object.
    /// </summary>
    private static byte[] DeriveKey(byte[] ikm)
    {
        // Use .NET built-in HKDF (available since .NET 5) to derive from concatenated DH outputs
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, HkdfSalt, HkdfInfo);
    }

    private static byte[] Concat(byte[] a, byte[] b, byte[] c)
    {
        var result = new byte[a.Length + b.Length + c.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        c.CopyTo(result, a.Length + b.Length);
        return result;
    }

    private static byte[] Concat(byte[] a, byte[] b, byte[] c, byte[] d)
    {
        var result = new byte[a.Length + b.Length + c.Length + d.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        c.CopyTo(result, a.Length + b.Length);
        d.CopyTo(result, a.Length + b.Length + c.Length);
        return result;
    }
}
