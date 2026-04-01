using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Messenger.Client.Avalonia.Models;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// Primary IEncryptionService implementation using X3DH session establishment
/// and Double Ratchet per-message encryption (Signal Protocol v1).
///
/// Legacy messages (ProtocolVersion.LegacyAes) are delegated to LocalEnvelopeEncryptionService
/// for backwards-compatible decryption (D-03).
///
/// After successful decryption, plaintext is persisted to messages.plaintext_body via
/// <see cref="ILocalMessageRepository.UpdatePlaintextBodyAsync"/> so the FTS5 search
/// index stays current (SRCH-01, Phase 05).
/// </summary>
public sealed class SignalProtocolEncryptionService(
    Crypto.IX3DHService x3dh,
    Crypto.ISessionManager sessionManager,
    Crypto.KeyPublicationService keyPublication,
    IMessengerApiClient apiClient,
    IClientSessionService sessionService,
    LocalEnvelopeEncryptionService legacyService,
    Crypto.IRatchetSessionRepository sessionRepository,
    Crypto.IIdentityKeyChangeDetector changeDetector,
    ILocalMessageRepository localMessageRepository) : IEncryptionService
{
    public async Task<EncryptedMessageDraft> EncryptTextAsync(
        string plaintext, Guid recipientUserId, CancellationToken cancellationToken = default)
    {
        // 1. Fetch peer key bundle
        var bundle = await apiClient.GetKeyBundleAsync(recipientUserId, cancellationToken);
        if (bundle is null)
        {
            throw new InvalidOperationException(
                "Could not establish a secure session. The recipient may not have published encryption keys yet.");
        }

        // 2. Session ID: "{localUserId}:{recipientUserId}" — stable per peer, device-agnostic for v1
        var sessionId = $"{sessionService.CurrentUserId}:{recipientUserId}";

        var existingSession = await sessionManager.GetSessionAsync(sessionId, cancellationToken);

        string? x3dhMetadataJson = null;

        if (existingSession is null)
        {
            // 3. Initiate X3DH
            var (sharedSecret, x3dhHeader) = x3dh.InitiateSession(
                keyPublication.LocalBundle,
                bundle.IkPublic,
                bundle.SpkPublic,
                bundle.SpkSignature,
                bundle.OpkPublic,
                bundle.OpkId);

            await sessionManager.CreateInitiatorSessionAsync(sessionId, sharedSecret, bundle.SpkPublic, cancellationToken);

            // Build X3DH portion of metadata
            x3dhMetadataJson = JsonSerializer.Serialize(new
            {
                ek_pub = Convert.ToBase64String(x3dhHeader.EkPub),
                opk_id = x3dhHeader.OpkId?.ToString(),
                ik_pub = Convert.ToBase64String(x3dhHeader.IkPub)
            });
        }

        // 4. Double Ratchet encrypt
        var associatedData = Encoding.UTF8.GetBytes($"{sessionService.CurrentUserId}:{recipientUserId}");
        var (ciphertext, ratchetPub, pn, n) = await sessionManager.EncryptAsync(
            sessionId,
            Encoding.UTF8.GetBytes(plaintext),
            associatedData,
            cancellationToken);

        // 5. Build metadata JSON
        var drObj = new
        {
            rk_pub = Convert.ToBase64String(ratchetPub),
            pn,
            n
        };

        string metadataJson;
        if (x3dhMetadataJson is not null)
        {
            metadataJson = JsonSerializer.Serialize(new
            {
                x3dh = JsonSerializer.Deserialize<JsonElement>(x3dhMetadataJson),
                dr = drObj
            });
        }
        else
        {
            metadataJson = JsonSerializer.Serialize(new { dr = drObj });
        }

        return new EncryptedMessageDraft(
            MessageKind.Text,
            Convert.ToBase64String(ciphertext),
            "signal-protocol-v1",
            sessionId,
            metadataJson);
    }

    public async Task<string> DecryptAsync(MessageDto message, CancellationToken cancellationToken = default)
    {
        // Per D-03: delegate legacy messages to LocalEnvelopeEncryptionService
        if (message.ProtocolVersion == ProtocolVersion.LegacyAes)
        {
            var legacyPlaintext = await legacyService.DecryptAsync(message, cancellationToken);
            // Persist plaintext for FTS5 search index (legacy path)
            if (!legacyPlaintext.StartsWith("[Unable to decrypt]", StringComparison.Ordinal))
            {
                await localMessageRepository.UpdatePlaintextBodyAsync(message.Id, legacyPlaintext, cancellationToken);
            }
            return legacyPlaintext;
        }

        try
        {
            if (message.MetadataJson is null)
            {
                return "[Unable to decrypt]";
            }

            using var metadata = JsonDocument.Parse(message.MetadataJson);
            var root = metadata.RootElement;

            // Session ID: "{senderId}:{currentUserId}" from receiver's perspective
            var sessionId = $"{message.SenderId}:{sessionService.CurrentUserId}";

            // If X3DH header present — establish responder session
            if (root.TryGetProperty("x3dh", out var x3dhElement))
            {
                var ekPub = Convert.FromBase64String(x3dhElement.GetProperty("ek_pub").GetString()!);
                var ikPub = Convert.FromBase64String(x3dhElement.GetProperty("ik_pub").GetString()!);

                // Identity key change detection (KEY-02)
                // Must run BEFORE CreateResponderSessionAsync to catch key substitution
                var verState = await sessionRepository.LoadVerificationStateAsync(sessionId, cancellationToken);
                if (verState.RemoteIkPublic is not null)
                {
                    // Stored IK exists — compare using constant-time equality (Pitfall 6: no SequenceEqual)
                    bool same = CryptographicOperations.FixedTimeEquals(verState.RemoteIkPublic, ikPub);
                    if (!same)
                    {
                        // Key change detected — reset verification state, store new IK, fire alert
                        await sessionRepository.SaveVerificationStateAsync(
                            sessionId,
                            verified: false,
                            lastVerifiedAt: null,
                            remoteIkPublic: ikPub,
                            cancellationToken);
                        await changeDetector.RaiseAsync(sessionId, ikPub);
                    }
                    // Keys match — no action needed (silent)
                }
                else
                {
                    // First time seeing this peer's IK — store silently, no alert (Pitfall 3)
                    await sessionRepository.SaveVerificationStateAsync(
                        sessionId,
                        verified: false,
                        lastVerifiedAt: null,
                        remoteIkPublic: ikPub,
                        cancellationToken);
                }

                Guid? opkId = null;
                byte[]? otpPrivate = null;

                if (x3dhElement.TryGetProperty("opk_id", out var opkIdElement) &&
                    opkIdElement.ValueKind != JsonValueKind.Null &&
                    opkIdElement.GetString() is { } opkIdStr &&
                    Guid.TryParse(opkIdStr, out var parsedOpkId))
                {
                    opkId = parsedOpkId;
                    var otpPair = keyPublication.FindOtpPrivateKey(parsedOpkId);
                    otpPrivate = otpPair?.Private;
                }

                var x3dhHeader = new Crypto.X3dhHeader(ekPub, opkId, ikPub);
                var sharedSecret = x3dh.RespondToSession(keyPublication.LocalBundle, otpPrivate, x3dhHeader);

                await sessionManager.CreateResponderSessionAsync(
                    sessionId,
                    sharedSecret,
                    keyPublication.LocalBundle.SpkPrivate,
                    keyPublication.LocalBundle.SpkPublic,
                    cancellationToken);
            }

            // Parse DR header
            var drElement = root.GetProperty("dr");
            var ratchetPub = Convert.FromBase64String(drElement.GetProperty("rk_pub").GetString()!);
            var pn = drElement.GetProperty("pn").GetInt32();
            var n = drElement.GetProperty("n").GetInt32();

            var ciphertext = Convert.FromBase64String(message.EncryptedPayload);
            var associatedData = Encoding.UTF8.GetBytes($"{message.SenderId}:{sessionService.CurrentUserId}");

            var plaintextBytes = await sessionManager.DecryptAsync(
                sessionId, ciphertext, ratchetPub, pn, n, associatedData, cancellationToken);

            var plaintext = Encoding.UTF8.GetString(plaintextBytes);

            // Persist plaintext for FTS5 search index (trigger updates messages_fts automatically)
            await localMessageRepository.UpdatePlaintextBodyAsync(message.Id, plaintext, cancellationToken);

            return plaintext;
        }
        catch (Exception)
        {
            return "[Unable to decrypt]";
        }
    }
}
