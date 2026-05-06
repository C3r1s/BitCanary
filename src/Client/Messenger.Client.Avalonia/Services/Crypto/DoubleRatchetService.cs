// Клиентское E2E: «DoubleRatchetService» (сессии, ключи, ratchet).
using System.Security.Cryptography;
using NSec.Cryptography;

namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed class DoubleRatchetService : IDoubleRatchetService
{
    private const int MaxSkip = 2000;
    private static readonly byte[] DhRatchetInfo = "DR-RK"u8.ToArray();
    [System.Runtime.InteropServices.DllImport("libsodium", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int crypto_scalarmult(byte[] q, byte[] n, byte[] p);


    public RatchetState InitializeInitiator(byte[] sharedSecret, byte[] remoteSpkPublic)
    {
        using var dhKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var dhPrivate = dhKey.Export(KeyBlobFormat.RawPrivateKey);
        var dhPublic = dhKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        var remoteSpkPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteSpkPublic, KeyBlobFormat.RawPublicKey);
        var dhOutput = PerformDH(dhPrivate, remoteSpkPub);

        var kdfOutput = KdfRk(sharedSecret, dhOutput);
        CryptographicOperations.ZeroMemory(dhOutput);

        var rootKey = kdfOutput[..32];
        var sendingChainKey = kdfOutput[32..];

        return new RatchetState
        {
            RootKey = rootKey,
            SendingChainKey = sendingChainKey,
            ReceivingChainKey = null,
            DhSendingPrivate = dhPrivate,
            DhSendingPublic = dhPublic,
            DhReceivingPublic = remoteSpkPublic,
            SendMessageNumber = 0,
            ReceiveMessageNumber = 0,
            PreviousSendingChainLength = 0
        };
    }

    public RatchetState InitializeResponder(byte[] sharedSecret, byte[] ownSpkPrivate, byte[] ownSpkPublic)
    {
        return new RatchetState
        {
            RootKey = sharedSecret.ToArray(), // copy to avoid aliasing
            SendingChainKey = null,
            ReceivingChainKey = null,
            DhSendingPrivate = ownSpkPrivate.ToArray(),
            DhSendingPublic = ownSpkPublic.ToArray(),
            DhReceivingPublic = null,
            SendMessageNumber = 0,
            ReceiveMessageNumber = 0,
            PreviousSendingChainLength = 0
        };
    }


    public (byte[] Ciphertext, byte[] RatchetPublic, int PreviousChainLength, int MessageNumber) Encrypt(
        RatchetState state,
        byte[] plaintext,
        byte[] associatedData)
    {
        if (state.SendingChainKey is null)
            throw new CryptographicException("Sending chain key not initialized");

        var messageKey = HmacSha256(state.SendingChainKey, [0x01]);
        state.SendingChainKey = HmacSha256(state.SendingChainKey, [0x02]);

        var nonce = MessageNumberToNonce(state.SendMessageNumber);

        var ciphertext = EncryptAead(messageKey, nonce, associatedData, plaintext);
        CryptographicOperations.ZeroMemory(messageKey);

        var result = (ciphertext, state.DhSendingPublic!.ToArray(), state.PreviousSendingChainLength, state.SendMessageNumber);
        state.SendMessageNumber++;

        return result;
    }


    public byte[] Decrypt(
        RatchetState state,
        byte[] ciphertext,
        byte[] ratchetPublic,
        int previousChainLength,
        int messageNumber,
        byte[] associatedData,
        Func<byte[], int, byte[]?> tryConsumeSkippedKey,
        Action<byte[], int, byte[]> storeSkippedKey)
    {
        var skippedKey = tryConsumeSkippedKey(ratchetPublic, messageNumber);
        if (skippedKey is not null)
        {
            var nonce = MessageNumberToNonce(messageNumber);
            var pt = DecryptAead(skippedKey, nonce, associatedData, ciphertext);
            CryptographicOperations.ZeroMemory(skippedKey);
            return pt;
        }

        bool isDhRatchetNeeded = state.DhReceivingPublic is null ||
                                 !state.DhReceivingPublic.SequenceEqual(ratchetPublic);

        if (isDhRatchetNeeded)
        {
            if (state.ReceivingChainKey is not null)
            {
                int skipCount = previousChainLength - state.ReceiveMessageNumber;
                if (skipCount > MaxSkip)
                    throw new CryptographicException($"Too many skipped messages: {skipCount} > {MaxSkip}");

                SkipMessageKeys(state, previousChainLength, storeSkippedKey);
            }

            state.PreviousSendingChainLength = state.SendMessageNumber;
            state.SendMessageNumber = 0;
            state.ReceiveMessageNumber = 0;
            state.DhReceivingPublic = ratchetPublic.ToArray();

            var remotePub = PublicKey.Import(KeyAgreementAlgorithm.X25519, ratchetPublic, KeyBlobFormat.RawPublicKey);
            var dhOutput1 = PerformDH(state.DhSendingPrivate!, remotePub);
            var kdf1 = KdfRk(state.RootKey!, dhOutput1);
            CryptographicOperations.ZeroMemory(dhOutput1);
            state.RootKey = kdf1[..32];
            state.ReceivingChainKey = kdf1[32..];

            using var newDhKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
            {
                ExportPolicy = KeyExportPolicies.AllowPlaintextExport
            });
            var newDhPrivate = newDhKey.Export(KeyBlobFormat.RawPrivateKey);
            var newDhPublic = newDhKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

            var dhOutput2 = PerformDH(newDhPrivate, remotePub);
            var kdf2 = KdfRk(state.RootKey, dhOutput2);
            CryptographicOperations.ZeroMemory(dhOutput2);
            state.RootKey = kdf2[..32];
            state.SendingChainKey = kdf2[32..];

            if (state.DhSendingPrivate is not null)
                CryptographicOperations.ZeroMemory(state.DhSendingPrivate);

            state.DhSendingPrivate = newDhPrivate;
            state.DhSendingPublic = newDhPublic;
        }

        int skipInNewChain = messageNumber - state.ReceiveMessageNumber;
        if (skipInNewChain > MaxSkip)
            throw new CryptographicException($"Too many skipped messages: {skipInNewChain} > {MaxSkip}");

        SkipMessageKeys(state, messageNumber, storeSkippedKey);

        if (state.ReceivingChainKey is null)
            throw new CryptographicException("Receiving chain key not initialized");

        var msgKey = HmacSha256(state.ReceivingChainKey, [0x01]);
        state.ReceivingChainKey = HmacSha256(state.ReceivingChainKey, [0x02]);
        state.ReceiveMessageNumber++;

        var msgNonce = MessageNumberToNonce(messageNumber);
        var plaintext = DecryptAead(msgKey, msgNonce, associatedData, ciphertext);
        CryptographicOperations.ZeroMemory(msgKey);
        return plaintext;
    }


    private static void SkipMessageKeys(
        RatchetState state,
        int targetMessageNumber,
        Action<byte[], int, byte[]> storeSkippedKey)
    {
        while (state.ReceiveMessageNumber < targetMessageNumber)
        {
            var skippedKey = HmacSha256(state.ReceivingChainKey!, [0x01]);
            state.ReceivingChainKey = HmacSha256(state.ReceivingChainKey!, [0x02]);
            storeSkippedKey(state.DhReceivingPublic!, state.ReceiveMessageNumber, skippedKey);
            state.ReceiveMessageNumber++;
        }
    }

    private static byte[] KdfRk(byte[] rootKey, byte[] dhOutput)
    {
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, dhOutput, 64, rootKey, DhRatchetInfo);
    }

    private static byte[] PerformDH(byte[] privateKeyBytes, PublicKey remotePublicKey)
    {
        var remotePublicBytes = remotePublicKey.Export(KeyBlobFormat.RawPublicKey);
        var shared = new byte[32];
        int result = crypto_scalarmult(shared, privateKeyBytes, remotePublicBytes);
        CryptographicOperations.ZeroMemory(remotePublicBytes);
        if (result != 0)
            throw new CryptographicException("X25519 DH agreement failed");
        return shared;
    }

    private static byte[] HmacSha256(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    private static byte[] MessageNumberToNonce(int messageNumber)
    {
        var nonce = new byte[12];
        var bytes = BitConverter.GetBytes((long)messageNumber);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        bytes.CopyTo(nonce, 0);
        return nonce;
    }

    private static byte[] EncryptAead(byte[] keyBytes, byte[] nonceBytes, byte[] associatedData, byte[] plaintext)
    {
        using var key = Key.Import(AeadAlgorithm.ChaCha20Poly1305, keyBytes, KeyBlobFormat.RawSymmetricKey);
        return AeadAlgorithm.ChaCha20Poly1305.Encrypt(key, nonceBytes, associatedData, plaintext);
    }

    private static byte[] DecryptAead(byte[] keyBytes, byte[] nonceBytes, byte[] associatedData, byte[] ciphertext)
    {
        using var key = Key.Import(AeadAlgorithm.ChaCha20Poly1305, keyBytes, KeyBlobFormat.RawSymmetricKey);
        var plaintext = AeadAlgorithm.ChaCha20Poly1305.Decrypt(key, nonceBytes, associatedData, ciphertext);
        if (plaintext == null)
            throw new CryptographicException("ChaCha20-Poly1305 decryption failed: authentication tag mismatch");
        return plaintext;
    }
}
