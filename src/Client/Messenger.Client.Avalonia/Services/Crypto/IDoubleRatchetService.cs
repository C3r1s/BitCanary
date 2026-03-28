namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Implements the Double Ratchet algorithm for per-message forward secrecy.
/// Encrypt/Decrypt are synchronous and stateful — callers must serialize
/// access via SessionManager's per-session SemaphoreSlim.
/// </summary>
public interface IDoubleRatchetService
{
    /// <summary>
    /// Called by the session initiator after X3DH. Derives the initial sending
    /// chain from the shared secret and the responder's SPK public key.
    /// </summary>
    RatchetState InitializeInitiator(byte[] sharedSecret, byte[] remoteSpkPublic);

    /// <summary>
    /// Called by the session responder on first incoming message. Sets the
    /// root key from the shared secret; the first DH ratchet step is deferred
    /// until the first Decrypt call.
    /// </summary>
    RatchetState InitializeResponder(byte[] sharedSecret, byte[] ownSpkPrivate, byte[] ownSpkPublic);

    /// <summary>
    /// Advances the sending chain, derives a per-message key, and encrypts plaintext
    /// with ChaCha20-Poly1305. Returns ciphertext plus DR header fields.
    /// </summary>
    (byte[] Ciphertext, byte[] RatchetPublic, int PreviousChainLength, int MessageNumber) Encrypt(
        RatchetState state, byte[] plaintext, byte[] associatedData);

    /// <summary>
    /// Decrypts a message. Checks skipped keys first; performs a DH ratchet step
    /// if the sender's ratchet public key has changed; advances the receiving chain.
    /// </summary>
    byte[] Decrypt(
        RatchetState state,
        byte[] ciphertext,
        byte[] ratchetPublic,
        int previousChainLength,
        int messageNumber,
        byte[] associatedData,
        Func<byte[], int, byte[]?> tryConsumeSkippedKey,
        Action<byte[], int, byte[]> storeSkippedKey);
}
