namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed record X3DHKeyBundle(
    byte[] IkPublic,
    byte[] IkPrivate,
    byte[] SpkPublic,
    byte[] SpkPrivate,
    byte[] SpkSignature,
    DateTimeOffset SpkCreatedAt);

public sealed record OtpKeyPair(byte[] Public, byte[] Private);
