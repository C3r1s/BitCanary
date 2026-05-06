// Проверка подписи signed pre-key в криптографическом бандле.
using Messenger.Application.Abstractions;
using NSec.Cryptography;

namespace Messenger.Infrastructure.Crypto;

public sealed class NSecSpkValidator : ISpkValidator
{
    public bool Validate(byte[] ikPublic, byte[] spkPublic, byte[] spkSignature)
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        if (!PublicKey.TryImport(algorithm, ikPublic, KeyBlobFormat.RawPublicKey, out var publicKey) || publicKey is null)
            return false;

        return algorithm.Verify(publicKey, spkPublic, spkSignature);
    }
}
