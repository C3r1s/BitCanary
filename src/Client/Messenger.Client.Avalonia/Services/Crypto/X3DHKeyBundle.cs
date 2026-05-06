// Клиентское E2E: «X3DHKeyBundle» (сессии, ключи, ratchet).
namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed record X3DHKeyBundle(
    byte[] IkPublic,
    byte[] IkPrivate,
    byte[] SpkPublic,
    byte[] SpkPrivate,
    byte[] SpkSignature,
    DateTimeOffset SpkCreatedAt);

public sealed record OtpKeyPair(byte[] Public, byte[] Private);

public sealed record NoiseHeader(
    byte[] EphemeralPub,
    byte[] StaticPub,
    byte[] Signature);
