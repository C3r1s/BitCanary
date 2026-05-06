// Клиентское E2E: «DrHeader» (сессии, ключи, ratchet).
namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed record DrHeader(byte[] RkPub, int Pn, int N);
