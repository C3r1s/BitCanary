// Клиентское E2E: «IX3DHService» (сессии, ключи, ratchet).
namespace Messenger.Client.Avalonia.Services.Crypto;

public interface IX3DHService
{
    X3DHKeyBundle GenerateKeyBundle();
    List<OtpKeyPair> GenerateOneTimePreKeys(int count);
    (byte[] SharedSecret, X3dhHeader Header) InitiateSession(
        X3DHKeyBundle localBundle,
        byte[] remoteIkPublic,
        byte[] remoteSpkPublic,
        byte[] remoteSpkSignature,
        byte[]? remoteOpkPublic,
        Guid? remoteOpkId);
    byte[] RespondToSession(
        X3DHKeyBundle localBundle,
        byte[]? localOpkPrivate,
        X3dhHeader incomingHeader);

    (byte[] SharedSecret, NoiseHeader Header) InitiateNoiseSession(
        X3DHKeyBundle localBundle,
        byte[] remoteIkPublic,
        byte[] remoteSpkPublic,
        byte[] remoteSpkSignature);

    byte[] RespondToNoiseSession(
        X3DHKeyBundle localBundle,
        byte[] remoteIkPublic,
        byte[] remoteSpkPublic,
        byte[] remoteSpkSignature,
        NoiseHeader incomingHeader);
}
