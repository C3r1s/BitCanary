namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed record X3dhHeader(byte[] EkPub, Guid? OpkId, byte[] IkPub);
