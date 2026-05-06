// Клиентское E2E: «IDoubleRatchetService» (сессии, ключи, ratchet).
namespace Messenger.Client.Avalonia.Services.Crypto;

public interface IDoubleRatchetService
{
    RatchetState InitializeInitiator(byte[] sharedSecret, byte[] remoteSpkPublic);

    RatchetState InitializeResponder(byte[] sharedSecret, byte[] ownSpkPrivate, byte[] ownSpkPublic);

    (byte[] Ciphertext, byte[] RatchetPublic, int PreviousChainLength, int MessageNumber) Encrypt(
        RatchetState state, byte[] plaintext, byte[] associatedData);

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
