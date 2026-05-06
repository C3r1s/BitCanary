// Клиентское E2E: «RatchetState» (сессии, ключи, ratchet).
using System.Text.Json;

namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed class RatchetState
{
    public byte[]? DhSendingPrivate { get; set; }   // Our current DH ratchet private key (X25519)
    public byte[]? DhSendingPublic { get; set; }    // Our current DH ratchet public key
    public byte[]? DhReceivingPublic { get; set; }  // Their current DH ratchet public key

    public byte[]? RootKey { get; set; }             // 32 bytes — root chain key
    public byte[]? SendingChainKey { get; set; }     // 32 bytes — current sending chain key
    public byte[]? ReceivingChainKey { get; set; }   // 32 bytes — current receiving chain key

    public int SendMessageNumber { get; set; }       // N_s — messages sent in current sending chain
    public int ReceiveMessageNumber { get; set; }    // N_r — messages received in current receiving chain
    public int PreviousSendingChainLength { get; set; } // P_n — length of previous sending chain

    public string SessionId { get; set; } = string.Empty; // "{peerId}:{deviceId}"

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static RatchetState Deserialize(byte[] blob) =>
        JsonSerializer.Deserialize<RatchetState>(blob)!;
}
