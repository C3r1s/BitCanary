// Клиентское E2E: «SignalProtocolEncryptionService» (сессии, ключи, ratchet).
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using Messenger.Client.Avalonia.Models;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.Extensions.Logging;

namespace Messenger.Client.Avalonia.Services;

public sealed class SignalProtocolEncryptionService(
    Crypto.IX3DHService x3dh,
    Crypto.ISessionManager sessionManager,
    Crypto.KeyPublicationService keyPublication,
    IMessengerApiClient apiClient,
    IClientSessionService sessionService,
    LocalEnvelopeEncryptionService legacyService,
    Crypto.IRatchetSessionRepository sessionRepository,
    Crypto.IIdentityKeyChangeDetector changeDetector,
    ILocalMessageRepository localMessageRepository,
    ILogger<SignalProtocolEncryptionService> logger) : IEncryptionService
{
    private static readonly Regex GuidRegex = new(
        @"\b[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[1-5][0-9a-fA-F]{3}\-[89abAB][0-9a-fA-F]{3}\-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);
    private static readonly Regex SessionEnvelopeRegex = new(
        @"\b[0-9a-fA-F\-]{36}:[0-9a-fA-F\-]{36}\b",
        RegexOptions.Compiled);
    private static readonly Regex Base64Regex = new(
        @"\b[A-Za-z0-9+/]{24,}={0,2}\b",
        RegexOptions.Compiled);
    private static readonly Regex SecretFieldRegex = new(
        @"\b(shared_secret_hash|ik|spk|opk|signature|seed|private|token)\s*=\s*[^ ]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string NormalizeGuid(Guid id) => id.ToString("D").ToLowerInvariant();
    private static string BuildSessionId(Guid left, Guid right) => $"{NormalizeGuid(left)}:{NormalizeGuid(right)}";
    private static byte[] BuildAssociatedData(Guid left, Guid right) =>
        Encoding.UTF8.GetBytes(BuildSessionId(left, right));
    private static string? NormalizeSessionIdEnvelope(string? keyEnvelope)
    {
        if (string.IsNullOrWhiteSpace(keyEnvelope)) return null;
        var parts = keyEnvelope.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;
        if (!Guid.TryParse(parts[0], out var left) || !Guid.TryParse(parts[1], out var right)) return null;
        return BuildSessionId(left, right);
    }
    private static bool IsSelfSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;
        var parts = sessionId.Split(':', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && string.Equals(parts[0], parts[1], StringComparison.Ordinal);
    }
    private static string ShortHash(byte[] bytes)
    {
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }

    private void LogCrypto(string stage, string message)
    {
        var safeMessage = RedactSensitive(message);
        logger.LogDebug("SignalCrypto {Stage}: {Message}", stage, safeMessage);
    }

    private static string RedactSensitive(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return message;
        var safe = message;
        safe = SessionEnvelopeRegex.Replace(safe, "[session]");
        safe = GuidRegex.Replace(safe, "[id]");
        safe = SecretFieldRegex.Replace(safe, m =>
        {
            var field = m.Value.Split('=')[0].Trim();
            return $"{field}=[redacted]";
        });
        safe = Base64Regex.Replace(safe, "[b64]");
        return safe;
    }

    public async Task<EncryptedMessageDraft> EncryptTextAsync(
        string plaintext, Guid recipientUserId, CancellationToken cancellationToken = default)
    {
        var sessionId = BuildSessionId(sessionService.CurrentUserId, recipientUserId);
        LogCrypto("encrypt-start",
            $"session={sessionId} sender={sessionService.CurrentUserId} recipient={recipientUserId} plaintextLen={plaintext.Length}");

        JsonElement? noiseElement;
        var bundle = await apiClient.GetKeyBundleAsync(recipientUserId, cancellationToken);
        if (bundle is null)
        {
            throw new InvalidOperationException(
                "Could not establish a secure session. The recipient may not have published encryption keys yet.");
        }
        var (sharedSecret, noiseHeader) = x3dh.InitiateNoiseSession(
            keyPublication.LocalBundle,
            bundle.IkPublic,
            bundle.SpkPublic,
            bundle.SpkSignature);
        LogCrypto("encrypt-noise-bootstrap",
            $"session={sessionId} remoteBundleSpkLen={bundle.SpkPublic.Length} remoteBundleIkLen={bundle.IkPublic.Length} shared_secret_hash={ShortHash(sharedSecret)}");
        await sessionManager.CreateInitiatorSessionAsync(sessionId, sharedSecret, bundle.SpkPublic, cancellationToken);
        noiseElement = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new
        {
            e_pub = Convert.ToBase64String(noiseHeader.EphemeralPub),
            s_pub = Convert.ToBase64String(noiseHeader.StaticPub),
            sig = Convert.ToBase64String(noiseHeader.Signature)
        }));

        var associatedData = BuildAssociatedData(sessionService.CurrentUserId, recipientUserId);
        LogCrypto("encrypt-ad-hash", $"session={sessionId} ad_hash={ShortHash(associatedData)}");
        var (ciphertext, ratchetPub, pn, n) = await sessionManager.EncryptAsync(
            sessionId,
            Encoding.UTF8.GetBytes(plaintext),
            associatedData,
            cancellationToken);
        LogCrypto("encrypt-dr",
            $"session={sessionId} n={n} pn={pn} ratchetPubLen={ratchetPub.Length} cipherLen={ciphertext.Length} rk_pub_hash={ShortHash(ratchetPub)}");

        var drObj = new
        {
            rk_pub = Convert.ToBase64String(ratchetPub),
            pn,
            n
        };

        var metadataJson = JsonSerializer.Serialize(new { noise = noiseElement.Value, dr = drObj });
        using (var guardDoc = JsonDocument.Parse(metadataJson))
        {
            if (!guardDoc.RootElement.TryGetProperty("noise", out _))
            {
                LogCrypto("encrypt-runtime-guard-failed", $"session={sessionId} missing noise metadata");
                throw new CryptographicException("Runtime guard: pv=3 metadata must contain noise bootstrap.");
            }
        }

        return new EncryptedMessageDraft(
            MessageKind.Text,
            Convert.ToBase64String(ciphertext),
            "noise-xx-dr-v1",
            sessionId,
            metadataJson);
    }

    public async Task<string> DecryptAsync(MessageDto message, CancellationToken cancellationToken = default)
    {
        if (message.SenderId == sessionService.CurrentUserId)
        {
            var ownPlaintext = await localMessageRepository
                .GetPlaintextBodyAsync(message.Id, cancellationToken);
            if (!string.IsNullOrEmpty(ownPlaintext))
            {
                LogCrypto("decrypt-own-restored", $"message={message.Id} plaintextLen={ownPlaintext.Length}");
                return ownPlaintext;
            }
            LogCrypto("decrypt-skip-own", $"message={message.Id} sender=self");
            return "[sent]";
        }

        if (message.ProtocolVersion == ProtocolVersion.Plaintext)
        {
            await localMessageRepository.SaveMessageAsync(message, (int)message.ProtocolVersion, cancellationToken);
            await localMessageRepository.UpdatePlaintextBodyAsync(message.Id, message.EncryptedPayload, cancellationToken);
            return message.EncryptedPayload;
        }

        if (message.ProtocolVersion == ProtocolVersion.LegacyAes)
        {
            var legacyPlaintext = await legacyService.DecryptAsync(message, cancellationToken);
            if (!legacyPlaintext.StartsWith("[Unable to decrypt]", StringComparison.Ordinal))
            {
                await localMessageRepository.SaveMessageAsync(message, (int)message.ProtocolVersion, cancellationToken);
                await localMessageRepository.UpdatePlaintextBodyAsync(message.Id, legacyPlaintext, cancellationToken);
            }
            return legacyPlaintext;
        }

        try
        {
            LogCrypto("decrypt-start",
                $"message={message.Id} sender={message.SenderId} pv={(int)message.ProtocolVersion} alg={message.EncryptionAlgorithm}");
            if (message.MetadataJson is null)
            {
                LogCrypto("decrypt", $"message={message.Id} metadataJson is null");
                return "[Unable to decrypt]";
            }

            using var metadata = JsonDocument.Parse(message.MetadataJson);
            var root = metadata.RootElement;

            var sessionId = BuildSessionId(message.SenderId, sessionService.CurrentUserId);
            var reverseSessionId = BuildSessionId(sessionService.CurrentUserId, message.SenderId);
            var envelopeSessionId = NormalizeSessionIdEnvelope(message.KeyEnvelope);
            var sessionCandidates = new[] { envelopeSessionId, sessionId, reverseSessionId }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => !IsSelfSession(x))
                .Distinct(StringComparer.Ordinal)
                .ToArray()!;
            if (sessionCandidates.Length == 0)
            {
                LogCrypto("decrypt-invalid-session", $"message={message.Id} keyEnvelope={message.KeyEnvelope ?? "n/a"}");
                return "[Unable to decrypt]";
            }

            if (root.TryGetProperty("noise", out var noiseElement))
            {
                LogCrypto("decrypt-noise-bootstrap",
                    $"message={message.Id} sender={message.SenderId} hasNoise=true");
                var senderBundle = await apiClient.GetKeyBundleAsync(message.SenderId, cancellationToken);
                if (senderBundle is null)
                    throw new CryptographicException("Sender bundle is unavailable for Noise verification.");

                var ikPub = senderBundle.IkPublic;

                var verState = await sessionRepository.LoadVerificationStateAsync(sessionId, cancellationToken);
                if (verState.RemoteIkPublic is not null)
                {
                    bool same = CryptographicOperations.FixedTimeEquals(verState.RemoteIkPublic, ikPub);
                    if (!same)
                    {
                        await sessionRepository.SaveVerificationStateAsync(
                            sessionId,
                            verified: false,
                            lastVerifiedAt: null,
                            remoteIkPublic: ikPub,
                            cancellationToken);
                        await changeDetector.RaiseAsync(sessionId, ikPub);
                    }
                }
                else
                {
                    await sessionRepository.SaveVerificationStateAsync(
                        sessionId,
                        verified: true,
                        lastVerifiedAt: DateTimeOffset.UtcNow,
                        remoteIkPublic: ikPub,
                        cancellationToken);
                    LogCrypto("tofu-verified", $"session={sessionId} first-seen IK auto-verified");
                }

                var noiseHeader = new Crypto.NoiseHeader(
                    Convert.FromBase64String(noiseElement.GetProperty("e_pub").GetString()!),
                    Convert.FromBase64String(noiseElement.GetProperty("s_pub").GetString()!),
                    Convert.FromBase64String(noiseElement.GetProperty("sig").GetString()!));
                var sharedSecret = x3dh.RespondToNoiseSession(
                    keyPublication.LocalBundle,
                    senderBundle.IkPublic,
                    senderBundle.SpkPublic,
                    senderBundle.SpkSignature,
                    noiseHeader);
                LogCrypto("decrypt-noise-secret",
                    $"message={message.Id} shared_secret_hash={ShortHash(sharedSecret)}");

                await sessionManager.CreateResponderSessionAsync(
                    sessionId,
                    sharedSecret,
                    keyPublication.LocalBundle.SpkPrivate,
                    keyPublication.LocalBundle.SpkPublic,
                    cancellationToken);
                LogCrypto("decrypt-noise-bootstrap-created", $"message={message.Id} session={sessionId}");
            }

            var drElement = root.GetProperty("dr");
            var ratchetPub = Convert.FromBase64String(drElement.GetProperty("rk_pub").GetString()!);
            var pn = drElement.GetProperty("pn").GetInt32();
            var n = drElement.GetProperty("n").GetInt32();

            var ciphertext = Convert.FromBase64String(message.EncryptedPayload);
            var associatedData = BuildAssociatedData(message.SenderId, sessionService.CurrentUserId);
            LogCrypto("decrypt-ad-hash", $"message={message.Id} ad_hash={ShortHash(associatedData)} rk_pub_hash={ShortHash(ratchetPub)}");

            byte[]? plaintextBytes = null;
            Exception? lastDecryptError = null;
            var reverseAd = BuildAssociatedData(sessionService.CurrentUserId, message.SenderId);
            foreach (var candidate in sessionCandidates)
            {
                try
                {
                    plaintextBytes = await sessionManager.DecryptAsync(
                        candidate, ciphertext, ratchetPub, pn, n, associatedData, cancellationToken);
                    LogCrypto("decrypt-dr-success",
                        $"message={message.Id} session={candidate} n={n} pn={pn} ad=primary");
                    break;
                }
                catch (CryptographicException ex1)
                {
                    lastDecryptError = ex1;
                    try
                    {
                        plaintextBytes = await sessionManager.DecryptAsync(
                            candidate, ciphertext, ratchetPub, pn, n, reverseAd, cancellationToken);
                        LogCrypto("decrypt-dr-success",
                            $"message={message.Id} session={candidate} n={n} pn={pn} ad=reverse");
                        break;
                    }
                    catch (CryptographicException ex2)
                    {
                        lastDecryptError = ex2;
                    }
                }
            }
            if (plaintextBytes is null)
            {
                if (root.TryGetProperty("noise", out var noiseRetryElement))
                {
                    LogCrypto("decrypt-rebuild-retry", $"message={message.Id} attempting responder rebuild after failure");
                    var senderBundle = await apiClient.GetKeyBundleAsync(message.SenderId, cancellationToken);
                    if (senderBundle is not null)
                    {
                        var retryHeader = new Crypto.NoiseHeader(
                            Convert.FromBase64String(noiseRetryElement.GetProperty("e_pub").GetString()!),
                            Convert.FromBase64String(noiseRetryElement.GetProperty("s_pub").GetString()!),
                            Convert.FromBase64String(noiseRetryElement.GetProperty("sig").GetString()!));
                        var retrySharedSecret = x3dh.RespondToNoiseSession(
                            keyPublication.LocalBundle,
                            senderBundle.IkPublic,
                            senderBundle.SpkPublic,
                            senderBundle.SpkSignature,
                            retryHeader);
                        await sessionManager.CreateResponderSessionAsync(
                            sessionId,
                            retrySharedSecret,
                            keyPublication.LocalBundle.SpkPrivate,
                            keyPublication.LocalBundle.SpkPublic,
                            cancellationToken);
                        plaintextBytes = await sessionManager.DecryptAsync(
                            sessionId, ciphertext, ratchetPub, pn, n, associatedData, cancellationToken);
                        LogCrypto("decrypt-rebuild-retry-success", $"message={message.Id} session={sessionId}");
                    }
                }
            }
            if (plaintextBytes is null)
            {
                throw lastDecryptError ?? new CryptographicException("Decrypt failed for all session-id candidates.");
            }

            var plaintext = Encoding.UTF8.GetString(plaintextBytes);

            await localMessageRepository.SaveMessageAsync(message, (int)message.ProtocolVersion, cancellationToken);
            await localMessageRepository.UpdatePlaintextBodyAsync(message.Id, plaintext, cancellationToken);
            LogCrypto("decrypt-success", $"message={message.Id} plaintextLen={plaintext.Length}");

            return plaintext;
        }
        catch (Exception ex)
        {
            var hasNoise = false;
            var n = "n/a";
            var pn = "n/a";
            try
            {
                if (!string.IsNullOrWhiteSpace(message.MetadataJson))
                {
                    using var hintDoc = JsonDocument.Parse(message.MetadataJson);
                    var hintRoot = hintDoc.RootElement;
                    hasNoise = hintRoot.TryGetProperty("noise", out _);
                    if (hintRoot.TryGetProperty("dr", out var drHint))
                    {
                        if (drHint.TryGetProperty("n", out var nProp))
                            n = nProp.GetInt32().ToString(CultureInfo.InvariantCulture);
                        if (drHint.TryGetProperty("pn", out var pnProp))
                            pn = pnProp.GetInt32().ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            catch
            {
            }

            LogCrypto("decrypt-failed",
                $"message={message.Id} sender={message.SenderId} protocol={(int)message.ProtocolVersion} " +
                $"alg={message.EncryptionAlgorithm} hasMeta={message.MetadataJson is not null} " +
                $"hasNoise={hasNoise} n={n} pn={pn} keyEnvelope={message.KeyEnvelope ?? "n/a"} " +
                $"error={ex.GetType().Name}: {ex.Message}");

            if (message.ProtocolVersion == ProtocolVersion.NoiseXX && !hasNoise)
            {
                try
                {
                    var sid1 = BuildSessionId(message.SenderId, sessionService.CurrentUserId);
                    var sid2 = BuildSessionId(sessionService.CurrentUserId, message.SenderId);
                    var sid3 = NormalizeSessionIdEnvelope(message.KeyEnvelope);
                    var resetCandidates = new[] { sid3, sid1, sid2 }
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Where(x => !IsSelfSession(x))
                        .Distinct(StringComparer.Ordinal);
                    foreach (var sid in resetCandidates)
                    {
                        await sessionManager.ResetSessionAsync(sid!, cancellationToken);
                        LogCrypto("decrypt-session-reset", $"message={message.Id} session={sid}");
                    }
                }
                catch (Exception resetEx)
                {
                    LogCrypto("decrypt-session-reset-failed",
                        $"message={message.Id} error={resetEx.GetType().Name}: {resetEx.Message}");
                }
            }
            return "[Unable to decrypt]";
        }
    }
}
