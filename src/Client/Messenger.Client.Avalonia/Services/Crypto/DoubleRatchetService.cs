using System.Security.Cryptography;
using NSec.Cryptography;

namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Implements the Signal Protocol Double Ratchet algorithm.
///
/// Symmetric ratchet: HMAC-SHA256(chainKey, 0x01) = messageKey,
///                    HMAC-SHA256(chainKey, 0x02) = nextChainKey
/// DH ratchet: X25519 DH + HKDF-SHA256 to derive new rootKey + chainKey
/// AEAD: ChaCha20-Poly1305 with 12-byte LE nonce from message number
/// </summary>
public sealed class DoubleRatchetService : IDoubleRatchetService
{
    private const int MaxSkip = 2000;
    private static readonly byte[] DhRatchetInfo = "DR-RK"u8.ToArray();

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public RatchetState InitializeInitiator(byte[] sharedSecret, byte[] remoteSpkPublic)
    {
        // Generate our initial DH ratchet key pair (X25519)
        using var dhKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var dhPrivate = dhKey.Export(KeyBlobFormat.RawPrivateKey);
        var dhPublic = dhKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        // Perform DH: our new ratchet key vs their SPK
        var remoteSpkPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, remoteSpkPublic, KeyBlobFormat.RawPublicKey);
        var dhOutput = PerformDH(dhPrivate, remoteSpkPub);

        // KDF_RK: HKDF(salt=sharedSecret, ikm=dhOutput, info="DR-RK", len=64)
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

    /// <inheritdoc/>
    public RatchetState InitializeResponder(byte[] sharedSecret, byte[] ownSpkPrivate, byte[] ownSpkPublic)
    {
        // Responder defers DH ratchet until first Decrypt call
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

    // -------------------------------------------------------------------------
    // Encrypt
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public (byte[] Ciphertext, byte[] RatchetPublic, int PreviousChainLength, int MessageNumber) Encrypt(
        RatchetState state,
        byte[] plaintext,
        byte[] associatedData)
    {
        if (state.SendingChainKey is null)
            throw new CryptographicException("Sending chain key not initialized");

        // Symmetric ratchet: derive message key and advance chain
        var messageKey = HmacSha256(state.SendingChainKey, [0x01]);
        state.SendingChainKey = HmacSha256(state.SendingChainKey, [0x02]);

        // Nonce: message number as 12-byte little-endian
        var nonce = MessageNumberToNonce(state.SendMessageNumber);

        // Encrypt with ChaCha20-Poly1305
        var ciphertext = EncryptAead(messageKey, nonce, associatedData, plaintext);
        CryptographicOperations.ZeroMemory(messageKey);

        // Capture header fields before incrementing
        var result = (ciphertext, state.DhSendingPublic!.ToArray(), state.PreviousSendingChainLength, state.SendMessageNumber);
        state.SendMessageNumber++;

        return result;
    }

    // -------------------------------------------------------------------------
    // Decrypt
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
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
        // Step 1: Check skipped keys
        var skippedKey = tryConsumeSkippedKey(ratchetPublic, messageNumber);
        if (skippedKey is not null)
        {
            var nonce = MessageNumberToNonce(messageNumber);
            var pt = DecryptAead(skippedKey, nonce, associatedData, ciphertext);
            CryptographicOperations.ZeroMemory(skippedKey);
            return pt;
        }

        // Step 2: DH ratchet step if we see a new ratchet public key
        bool isDhRatchetNeeded = state.DhReceivingPublic is null ||
                                 !state.DhReceivingPublic.SequenceEqual(ratchetPublic);

        if (isDhRatchetNeeded)
        {
            // Validate skip limit for old receiving chain
            if (state.ReceivingChainKey is not null)
            {
                int skipCount = previousChainLength - state.ReceiveMessageNumber;
                if (skipCount > MaxSkip)
                    throw new CryptographicException($"Too many skipped messages: {skipCount} > {MaxSkip}");

                // Store skipped keys from the old receiving chain
                SkipMessageKeys(state, previousChainLength, storeSkippedKey);
            }

            // Reset counters for the new ratchet epoch
            state.PreviousSendingChainLength = state.SendMessageNumber;
            state.SendMessageNumber = 0;
            state.ReceiveMessageNumber = 0;
            state.DhReceivingPublic = ratchetPublic.ToArray();

            // DH(our sending private, their new public) => new receiving chain
            var remotePub = PublicKey.Import(KeyAgreementAlgorithm.X25519, ratchetPublic, KeyBlobFormat.RawPublicKey);
            var dhOutput1 = PerformDH(state.DhSendingPrivate!, remotePub);
            var kdf1 = KdfRk(state.RootKey!, dhOutput1);
            CryptographicOperations.ZeroMemory(dhOutput1);
            state.RootKey = kdf1[..32];
            state.ReceivingChainKey = kdf1[32..];

            // Generate new DH key pair for our next sending ratchet
            using var newDhKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
            {
                ExportPolicy = KeyExportPolicies.AllowPlaintextExport
            });
            var newDhPrivate = newDhKey.Export(KeyBlobFormat.RawPrivateKey);
            var newDhPublic = newDhKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

            // DH(new sending private, their new public) => new sending chain
            var dhOutput2 = PerformDH(newDhPrivate, remotePub);
            var kdf2 = KdfRk(state.RootKey, dhOutput2);
            CryptographicOperations.ZeroMemory(dhOutput2);
            state.RootKey = kdf2[..32];
            state.SendingChainKey = kdf2[32..];

            // Zero old DH private key before replacing
            if (state.DhSendingPrivate is not null)
                CryptographicOperations.ZeroMemory(state.DhSendingPrivate);

            state.DhSendingPrivate = newDhPrivate;
            state.DhSendingPublic = newDhPublic;
        }

        // Step 3: Skip messages in the new receiving chain to reach messageNumber
        int skipInNewChain = messageNumber - state.ReceiveMessageNumber;
        if (skipInNewChain > MaxSkip)
            throw new CryptographicException($"Too many skipped messages: {skipInNewChain} > {MaxSkip}");

        SkipMessageKeys(state, messageNumber, storeSkippedKey);

        // Step 4: Symmetric ratchet — derive the message key
        if (state.ReceivingChainKey is null)
            throw new CryptographicException("Receiving chain key not initialized");

        var msgKey = HmacSha256(state.ReceivingChainKey, [0x01]);
        state.ReceivingChainKey = HmacSha256(state.ReceivingChainKey, [0x02]);
        state.ReceiveMessageNumber++;

        // Step 5: Decrypt
        var msgNonce = MessageNumberToNonce(messageNumber);
        var plaintext = DecryptAead(msgKey, msgNonce, associatedData, ciphertext);
        CryptographicOperations.ZeroMemory(msgKey);
        return plaintext;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Advances the receiving chain from current ReceiveMessageNumber up to (but not including)
    /// targetMessageNumber, storing each skipped message key.
    /// </summary>
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

    /// <summary>
    /// KDF_RK: HKDF-SHA-256(salt=rootKey, ikm=dhOutput, info="DR-RK", outputLen=64).
    /// Returns 64 bytes: first 32 = new root key, next 32 = chain key.
    /// </summary>
    private static byte[] KdfRk(byte[] rootKey, byte[] dhOutput)
    {
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, dhOutput, 64, rootKey, DhRatchetInfo);
    }

    /// <summary>
    /// Performs X25519 DH from a raw 32-byte private key and an NSec PublicKey.
    /// Extracts the raw 32-byte DH output via HKDF with empty salt/info.
    /// </summary>
    private static byte[] PerformDH(byte[] privateKeyBytes, PublicKey remotePublicKey)
    {
        using var privateKey = Key.Import(KeyAgreementAlgorithm.X25519, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
        using var sharedSecret = KeyAgreementAlgorithm.X25519.Agree(privateKey, remotePublicKey)
            ?? throw new CryptographicException("X25519 DH agreement failed");
        // Extract 32-byte raw DH output by using HKDF extract with empty salt/info
        return KeyDerivationAlgorithm.HkdfSha256.DeriveBytes(sharedSecret, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, 32);
    }

    /// <summary>
    /// HMAC-SHA256(key, data) using .NET built-in HMACSHA256.
    /// </summary>
    private static byte[] HmacSha256(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    /// <summary>
    /// Encodes a message number as a 12-byte little-endian nonce for ChaCha20-Poly1305.
    /// </summary>
    private static byte[] MessageNumberToNonce(int messageNumber)
    {
        var nonce = new byte[12];
        var bytes = BitConverter.GetBytes((long)messageNumber);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        // Copy 8 bytes of the long into the first 8 bytes of the 12-byte nonce
        bytes.CopyTo(nonce, 0);
        return nonce;
    }

    /// <summary>
    /// ChaCha20-Poly1305 encryption using NSec.
    /// Nonce is passed as a ReadOnlySpan (12 bytes).
    /// </summary>
    private static byte[] EncryptAead(byte[] keyBytes, byte[] nonceBytes, byte[] associatedData, byte[] plaintext)
    {
        using var key = Key.Import(AeadAlgorithm.ChaCha20Poly1305, keyBytes, KeyBlobFormat.RawSymmetricKey);
        return AeadAlgorithm.ChaCha20Poly1305.Encrypt(key, nonceBytes, associatedData, plaintext);
    }

    /// <summary>
    /// ChaCha20-Poly1305 decryption using NSec.
    /// Decrypt returns a Span; we convert to byte[] for safe storage.
    /// </summary>
    private static byte[] DecryptAead(byte[] keyBytes, byte[] nonceBytes, byte[] associatedData, byte[] ciphertext)
    {
        using var key = Key.Import(AeadAlgorithm.ChaCha20Poly1305, keyBytes, KeyBlobFormat.RawSymmetricKey);
        var plaintext = AeadAlgorithm.ChaCha20Poly1305.Decrypt(key, nonceBytes, associatedData, ciphertext);
        if (plaintext == null)
            throw new CryptographicException("ChaCha20-Poly1305 decryption failed: authentication tag mismatch");
        return plaintext;
    }
}
