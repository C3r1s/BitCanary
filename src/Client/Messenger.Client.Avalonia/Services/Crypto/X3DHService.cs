// Клиентское E2E: «X3DHService» (сессии, ключи, ratchet).
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed class X3DHService : IX3DHService
{
    private static readonly byte[] HkdfSalt = new byte[32]; // 32 zero bytes
    private static readonly byte[] HkdfInfo = "X3DH"u8.ToArray();
    private static readonly byte[] NoiseInfo = "Noise_XX_25519_ChaChaPoly_SHA256"u8.ToArray();

    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl)]
    private static extern int crypto_sign_ed25519_pk_to_curve25519(byte[] x25519_pk, byte[] ed25519_pk);

    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl)]
    private static extern int crypto_sign_ed25519_sk_to_curve25519(byte[] x25519_sk, byte[] ed25519_sk);

    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl)]
    private static extern int crypto_scalarmult(byte[] q, byte[] n, byte[] p);

    public X3DHKeyBundle GenerateKeyBundle()
    {
        using var ikKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var ikPublic = ikKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var ikPrivate = ikKey.Export(KeyBlobFormat.RawPrivateKey); // 32-byte seed

        using var spkKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var spkPublic = spkKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var spkPrivate = spkKey.Export(KeyBlobFormat.RawPrivateKey);

        var spkSignature = SignatureAlgorithm.Ed25519.Sign(ikKey, spkPublic);

        return new X3DHKeyBundle(
            ikPublic,
            ikPrivate,
            spkPublic,
            spkPrivate,
            spkSignature,
            DateTimeOffset.UtcNow);
    }

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

    public (byte[] SharedSecret, X3dhHeader Header) InitiateSession(
        X3DHKeyBundle localBundle,
        byte[] remoteIkPublic,
        byte[] remoteSpkPublic,
        byte[] remoteSpkSignature,
        byte[]? remoteOpkPublic,
        Guid? remoteOpkId)
    {
        var remoteIkPub = PublicKey.Import(SignatureAlgorithm.Ed25519, remoteIkPublic, KeyBlobFormat.RawPublicKey);
        if (!SignatureAlgorithm.Ed25519.Verify(remoteIkPub, remoteSpkPublic, remoteSpkSignature))
            throw new CryptographicException("Invalid SPK signature");

        using var ekKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var ekPublic = ekKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var ekPrivate = ekKey.Export(KeyBlobFormat.RawPrivateKey);

        var remoteSpkPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteSpkPublic, KeyBlobFormat.RawPublicKey);

        var localIkX25519Private = ConvertEd25519PrivateToX25519(localBundle.IkPrivate, localBundle.IkPublic);

        var remoteIkX25519Public = ConvertEd25519PublicToX25519(remoteIkPublic);

        var dh1 = PerformDH(localIkX25519Private, remoteSpkPub);

        var remoteIkX25519Pub = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteIkX25519Public, KeyBlobFormat.RawPublicKey);
        var dh2 = PerformDH(ekPrivate, remoteIkX25519Pub);

        var dh3 = PerformDH(ekPrivate, remoteSpkPub);

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

        var sk = DeriveKey(ikm);

        CryptographicOperations.ZeroMemory(dh1);
        CryptographicOperations.ZeroMemory(dh2);
        CryptographicOperations.ZeroMemory(dh3);
        CryptographicOperations.ZeroMemory(ikm);
        CryptographicOperations.ZeroMemory(localIkX25519Private);

        var header = new X3dhHeader(ekPublic, remoteOpkId, localBundle.IkPublic);
        return (sk, header);
    }

    public byte[] RespondToSession(
        X3DHKeyBundle localBundle,
        byte[]? localOpkPrivate,
        X3dhHeader incomingHeader)
    {
        var ekPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, incomingHeader.EkPub, KeyBlobFormat.RawPublicKey);
        var remoteSpkPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, localBundle.SpkPublic, KeyBlobFormat.RawPublicKey);

        var localIkX25519Private = ConvertEd25519PrivateToX25519(localBundle.IkPrivate, localBundle.IkPublic);

        var remoteIkX25519Public = ConvertEd25519PublicToX25519(incomingHeader.IkPub);
        var remoteIkX25519Pub = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteIkX25519Public, KeyBlobFormat.RawPublicKey);

        var dh1 = PerformDH(localBundle.SpkPrivate, remoteIkX25519Pub);

        var dh2 = PerformDH(localIkX25519Private, ekPub);

        var dh3 = PerformDH(localBundle.SpkPrivate, ekPub);

        byte[] ikm;
        if (localOpkPrivate != null)
        {
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

    public (byte[] SharedSecret, NoiseHeader Header) InitiateNoiseSession(
        X3DHKeyBundle localBundle,
        byte[] remoteIkPublic,
        byte[] remoteSpkPublic,
        byte[] remoteSpkSignature)
    {
        var remoteIkPub = PublicKey.Import(SignatureAlgorithm.Ed25519, remoteIkPublic, KeyBlobFormat.RawPublicKey);
        if (!SignatureAlgorithm.Ed25519.Verify(remoteIkPub, remoteSpkPublic, remoteSpkSignature))
            throw new CryptographicException("Invalid remote SPK signature");

        using var ephemeral = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var ePub = ephemeral.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var ePriv = ephemeral.Export(KeyBlobFormat.RawPrivateKey);
        var sPub = localBundle.SpkPublic;

        var remoteStaticPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteSpkPublic, KeyBlobFormat.RawPublicKey);
        var ikm = PerformDH(localBundle.SpkPrivate, remoteStaticPub);
        var shared = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, HkdfSalt, NoiseInfo);

        using var localIk = Key.Import(SignatureAlgorithm.Ed25519, localBundle.IkPrivate, KeyBlobFormat.RawPrivateKey);
        var signature = SignatureAlgorithm.Ed25519.Sign(localIk, Concat(ePub, sPub));

        CryptographicOperations.ZeroMemory(ikm);
        CryptographicOperations.ZeroMemory(ePriv);
        return (shared, new NoiseHeader(ePub, sPub, signature));
    }

    public byte[] RespondToNoiseSession(
        X3DHKeyBundle localBundle,
        byte[] remoteIkPublic,
        byte[] remoteSpkPublic,
        byte[] remoteSpkSignature,
        NoiseHeader incomingHeader)
    {
        var remoteIkPub = PublicKey.Import(SignatureAlgorithm.Ed25519, remoteIkPublic, KeyBlobFormat.RawPublicKey);
        if (!SignatureAlgorithm.Ed25519.Verify(remoteIkPub, remoteSpkPublic, remoteSpkSignature))
            throw new CryptographicException("Invalid remote SPK signature");

        if (!CryptographicOperations.FixedTimeEquals(incomingHeader.StaticPub, remoteSpkPublic))
            throw new CryptographicException("Noise static key mismatch with remote bundle");

        if (!SignatureAlgorithm.Ed25519.Verify(remoteIkPub, Concat(incomingHeader.EphemeralPub, incomingHeader.StaticPub), incomingHeader.Signature))
            throw new CryptographicException("Invalid Noise handshake signature");

        var remoteStaticPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, incomingHeader.StaticPub, KeyBlobFormat.RawPublicKey);
        var ikm = PerformDH(localBundle.SpkPrivate, remoteStaticPub);
        var shared = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, HkdfSalt, NoiseInfo);
        CryptographicOperations.ZeroMemory(ikm);
        return shared;
    }


    private static byte[] ConvertEd25519PublicToX25519(byte[] ed25519Pk)
    {
        var x25519Pk = new byte[32];
        int result = crypto_sign_ed25519_pk_to_curve25519(x25519Pk, ed25519Pk);
        if (result != 0)
            throw new CryptographicException("Ed25519 public key to X25519 conversion failed");
        return x25519Pk;
    }

    private static byte[] ConvertEd25519PrivateToX25519(byte[] ed25519Seed, byte[] ed25519Public)
    {
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

    private static byte[] PerformDH(byte[] privateKeyBytes, PublicKey publicKey)
    {
        var publicKeyBytes = publicKey.Export(KeyBlobFormat.RawPublicKey);
        var shared = new byte[32];
        int result = crypto_scalarmult(shared, privateKeyBytes, publicKeyBytes);
        CryptographicOperations.ZeroMemory(publicKeyBytes);
        if (result != 0)
            throw new CryptographicException("DH agreement failed");
        return shared;
    }

    private static byte[] DeriveKey(byte[] ikm)
    {
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

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}
