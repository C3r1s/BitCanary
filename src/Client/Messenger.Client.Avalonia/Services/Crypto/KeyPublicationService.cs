using Messenger.Shared.Contracts.Dtos;
using Microsoft.Extensions.Logging;

namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Manages the lifecycle of the local X3DH key bundle:
/// - Generates and uploads a new bundle on first login (D-07, D-08)
/// - Rotates the SPK after 7 days (D-10)
/// - Replenishes OPKs when the server signals low supply (D-09)
///
/// Private key material is DPAPI-protected via IKeyStore before being
/// stored in ILocalCacheService.
/// </summary>
public sealed class KeyPublicationService(
    IX3DHService x3dh,
    IKeyStore keyStore,
    ILocalCacheService localCacheService,
    IMessengerApiClient apiClient,
    ILogger<KeyPublicationService> logger)
{
    private const string DeviceIdCacheKey = "device-id";
    private const string IkPrivateCacheKey = "ik-private-dpapi";
    private const string SpkPrivateCacheKey = "spk-private-dpapi";
    private const string OtpPrivatesCacheKey = "otp-privates-dpapi";
    private const string SpkCreatedAtCacheKey = "spk-created-at";
    private const string IkPublicCacheKey = "ik-public";
    private const string SpkPublicCacheKey = "spk-public";
    private const string SpkSignatureCacheKey = "spk-signature";
    private const int InitialOtpkCount = 20;
    private const int ReplenishOtpkCount = 20;
    private static readonly TimeSpan SpkRotationInterval = TimeSpan.FromDays(7);

    private X3DHKeyBundle? _localBundle;

    // Maps base64-encoded OPK public key -> DPAPI-protected base64 private key.
    // We use base64 public key as map key because IDs are assigned server-side.
    // For lookup by server-assigned Guid, we scan by matching public key bytes.
    private Dictionary<string, string> _otpPrivates = new();

    // Maps Guid (server-assigned OPK ID) -> OtpKeyPair, populated after upload.
    private Dictionary<Guid, OtpKeyPair> _otpById = new();

    /// <summary>The local X3DH key bundle. Available after EnsureKeyBundlePublishedAsync.</summary>
    public X3DHKeyBundle LocalBundle =>
        _localBundle ?? throw new InvalidOperationException(
            "Key bundle not loaded. Call EnsureKeyBundlePublishedAsync first.");

    /// <summary>Looks up the OTP private key by server-assigned OPK ID.</summary>
    public OtpKeyPair? FindOtpPrivateKey(Guid opkId) =>
        _otpById.TryGetValue(opkId, out var pair) ? pair : null;

    /// <summary>
    /// Ensures the local key bundle is published to the server.
    /// On first run: generates, uploads, and persists.
    /// On subsequent runs: loads from cache and checks SPK rotation.
    /// Failures are logged as warnings — NOT thrown (D-09).
    /// </summary>
    public async Task EnsureKeyBundlePublishedAsync(CancellationToken ct = default)
    {
        try
        {
            var existingIkPriv = await localCacheService.LoadAsync<string>(IkPrivateCacheKey, ct);

            if (existingIkPriv is null)
            {
                await GenerateAndUploadBundleAsync(ct);
            }
            else
            {
                await LoadBundleFromCacheAsync(ct);
                await CheckSpkRotationAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Key bundle publication failed — messages may use legacy encryption.");
        }
    }

    /// <summary>
    /// Replenishes OPKs when the server signals the pool is low (D-09).
    /// </summary>
    public async Task ReplenishOtpksAsync(CancellationToken ct = default)
    {
        try
        {
            var deviceIdStr = await localCacheService.LoadAsync<string>(DeviceIdCacheKey, ct);
            if (deviceIdStr is null)
            {
                logger.LogWarning("Cannot replenish OTPs — no device ID found in cache.");
                return;
            }

            var deviceId = Guid.Parse(deviceIdStr);
            var newOtps = x3dh.GenerateOneTimePreKeys(ReplenishOtpkCount);
            await apiClient.ReplenishOtpksAsync(
                new OtpkReplenishRequest(deviceId, newOtps.Select(k => k.Public).ToArray()), ct);

            // Merge into in-memory store (without server-assigned IDs we can only store by public key)
            foreach (var otp in newOtps)
            {
                var pubKey = Convert.ToBase64String(otp.Public);
                _otpPrivates[pubKey] = keyStore.ProtectToBase64(otp.Private);
            }

            await localCacheService.SaveAsync(OtpPrivatesCacheKey, _otpPrivates, ct);
            logger.LogInformation("OPK pool replenished with {Count} new keys.", ReplenishOtpkCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OPK replenishment failed.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task GenerateAndUploadBundleAsync(CancellationToken ct)
    {
        logger.LogInformation("No local key bundle found — generating new bundle.");

        _localBundle = x3dh.GenerateKeyBundle();
        var otpKeys = x3dh.GenerateOneTimePreKeys(InitialOtpkCount);

        // Upload identity + pre-key bundle
        var resp = await apiClient.UploadKeyBundleAsync(
            new KeyBundleUploadRequest(
                null,
                _localBundle.IkPublic,
                _localBundle.SpkPublic,
                _localBundle.SpkSignature),
            ct);

        // Upload OPKs
        await apiClient.ReplenishOtpksAsync(
            new OtpkReplenishRequest(resp.DeviceId, otpKeys.Select(k => k.Public).ToArray()),
            ct);

        // Persist device ID
        await localCacheService.SaveAsync(DeviceIdCacheKey, resp.DeviceId.ToString(), ct);

        // Persist DPAPI-protected private keys
        await localCacheService.SaveAsync(IkPrivateCacheKey, keyStore.ProtectToBase64(_localBundle.IkPrivate), ct);
        await localCacheService.SaveAsync(SpkPrivateCacheKey, keyStore.ProtectToBase64(_localBundle.SpkPrivate), ct);
        await localCacheService.SaveAsync(IkPublicCacheKey, Convert.ToBase64String(_localBundle.IkPublic), ct);
        await localCacheService.SaveAsync(SpkPublicCacheKey, Convert.ToBase64String(_localBundle.SpkPublic), ct);
        await localCacheService.SaveAsync(SpkSignatureCacheKey, Convert.ToBase64String(_localBundle.SpkSignature), ct);
        await localCacheService.SaveAsync(SpkCreatedAtCacheKey, _localBundle.SpkCreatedAt.ToString("O"), ct);

        // Persist OTP private keys (DPAPI-protected) keyed by base64 public key
        _otpPrivates = otpKeys.ToDictionary(
            k => Convert.ToBase64String(k.Public),
            k => keyStore.ProtectToBase64(k.Private));
        await localCacheService.SaveAsync(OtpPrivatesCacheKey, _otpPrivates, ct);

        logger.LogInformation("Key bundle published. DeviceId={DeviceId}", resp.DeviceId);
    }

    private async Task LoadBundleFromCacheAsync(CancellationToken ct)
    {
        var ikPrivDpapi = await localCacheService.LoadAsync<string>(IkPrivateCacheKey, ct)
                          ?? throw new InvalidOperationException("IK private key missing from cache.");
        var spkPrivDpapi = await localCacheService.LoadAsync<string>(SpkPrivateCacheKey, ct)
                           ?? throw new InvalidOperationException("SPK private key missing from cache.");
        var ikPubB64 = await localCacheService.LoadAsync<string>(IkPublicCacheKey, ct)
                       ?? throw new InvalidOperationException("IK public key missing from cache.");
        var spkPubB64 = await localCacheService.LoadAsync<string>(SpkPublicCacheKey, ct)
                        ?? throw new InvalidOperationException("SPK public key missing from cache.");
        var spkSigB64 = await localCacheService.LoadAsync<string>(SpkSignatureCacheKey, ct)
                        ?? throw new InvalidOperationException("SPK signature missing from cache.");
        var spkCreatedAtStr = await localCacheService.LoadAsync<string>(SpkCreatedAtCacheKey, ct)
                              ?? throw new InvalidOperationException("SPK creation time missing from cache.");

        _localBundle = new X3DHKeyBundle(
            IkPublic: Convert.FromBase64String(ikPubB64),
            IkPrivate: keyStore.UnprotectFromBase64(ikPrivDpapi),
            SpkPublic: Convert.FromBase64String(spkPubB64),
            SpkPrivate: keyStore.UnprotectFromBase64(spkPrivDpapi),
            SpkSignature: Convert.FromBase64String(spkSigB64),
            SpkCreatedAt: DateTimeOffset.Parse(spkCreatedAtStr));

        _otpPrivates = await localCacheService.LoadAsync<Dictionary<string, string>>(OtpPrivatesCacheKey, ct)
                       ?? new Dictionary<string, string>();

        // Reconstruct _otpById from _otpPrivates (public key lookup only — server IDs not cached locally)
        _otpById = _otpPrivates.ToDictionary(
            kvp => Guid.Empty, // placeholder — actual IDs require server round-trip
            kvp => new OtpKeyPair(
                Convert.FromBase64String(kvp.Key),
                keyStore.UnprotectFromBase64(kvp.Value)));

        logger.LogDebug("Local key bundle loaded from cache. SPK created at {SpkCreatedAt}.", _localBundle.SpkCreatedAt);
    }

    private async Task CheckSpkRotationAsync(CancellationToken ct)
    {
        if (_localBundle is null) return;

        if (DateTimeOffset.UtcNow - _localBundle.SpkCreatedAt <= SpkRotationInterval)
        {
            return;
        }

        logger.LogInformation("SPK is older than 7 days — rotating.");

        var deviceIdStr = await localCacheService.LoadAsync<string>(DeviceIdCacheKey, ct);
        if (deviceIdStr is null)
        {
            logger.LogWarning("Cannot rotate SPK — no device ID found in cache.");
            return;
        }

        var deviceId = Guid.Parse(deviceIdStr);

        // Generate new SPK
        var newBundle = x3dh.GenerateKeyBundle();

        // Build rotated bundle reusing existing IK
        var rotated = new X3DHKeyBundle(
            IkPublic: _localBundle.IkPublic,
            IkPrivate: _localBundle.IkPrivate,
            SpkPublic: newBundle.SpkPublic,
            SpkPrivate: newBundle.SpkPrivate,
            SpkSignature: newBundle.SpkSignature,
            SpkCreatedAt: DateTimeOffset.UtcNow);

        await apiClient.UploadKeyBundleAsync(
            new KeyBundleUploadRequest(
                deviceId,
                rotated.IkPublic,
                rotated.SpkPublic,
                rotated.SpkSignature),
            ct);

        // Persist updated bundle
        _localBundle = rotated;
        await localCacheService.SaveAsync(SpkPrivateCacheKey, keyStore.ProtectToBase64(rotated.SpkPrivate), ct);
        await localCacheService.SaveAsync(SpkPublicCacheKey, Convert.ToBase64String(rotated.SpkPublic), ct);
        await localCacheService.SaveAsync(SpkSignatureCacheKey, Convert.ToBase64String(rotated.SpkSignature), ct);
        await localCacheService.SaveAsync(SpkCreatedAtCacheKey, rotated.SpkCreatedAt.ToString("O"), ct);

        logger.LogInformation("SPK rotation complete.");
    }
}
