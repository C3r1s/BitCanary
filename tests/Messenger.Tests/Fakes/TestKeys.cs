// Автотест BitCanary: проверка «TestKeys».
using NSec.Cryptography;

namespace Messenger.Tests.Fakes;

public static class TestKeys
{
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
