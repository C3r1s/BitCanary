using System.Text.Json;

namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Holds the full mutable state of a Double Ratchet session for one peer.
/// Serialized to JSON and persisted in the ratchet_sessions SQLite table.
/// </summary>
public sealed class RatchetState
{
    // DH ratchet keys (current)
    public byte[]? DhSendingPrivate { get; set; }   // Our current DH ratchet private key (X25519)
    public byte[]? DhSendingPublic { get; set; }    // Our current DH ratchet public key
    public byte[]? DhReceivingPublic { get; set; }  // Their current DH ratchet public key

    // Chain keys
    public byte[]? RootKey { get; set; }             // 32 bytes — root chain key
    public byte[]? SendingChainKey { get; set; }     // 32 bytes — current sending chain key
    public byte[]? ReceivingChainKey { get; set; }   // 32 bytes — current receiving chain key

    // Counters
    public int SendMessageNumber { get; set; }       // N_s — messages sent in current sending chain
    public int ReceiveMessageNumber { get; set; }    // N_r — messages received in current receiving chain
    public int PreviousSendingChainLength { get; set; } // P_n — length of previous sending chain

    // Session identity
    public string SessionId { get; set; } = string.Empty; // "{peerId}:{deviceId}"

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static RatchetState Deserialize(byte[] blob) =>
        JsonSerializer.Deserialize<RatchetState>(blob)!;
}
