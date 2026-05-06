// Клиентское E2E: «KeyPublicationService» (сессии, ключи, ratchet).
using Messenger.Shared.Contracts.Dtos;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using System.Security.Cryptography;

namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed class KeyPublicationService(
    IX3DHService x3dh,
    IKeyStore keyStore,
    ILocalCacheService localCacheService,
    IMessengerApiClient apiClient,
    IClientSessionService sessionService,
    ILogger<KeyPublicationService> logger)
{
    private const string DeviceIdCacheKey = "device-id";
    private const string IkPrivateCacheKey = "ik-private-dpapi";
    private const string SpkPrivateCacheKey = "spk-private-dpapi";
    private const string OtpPrivatesCacheKey = "otp-privates-dpapi";
    private const string OtpIdMapCacheKey = "otp-id-map-dpapi";
    private const string SpkCreatedAtCacheKey = "spk-created-at";
    private const string IkPublicCacheKey = "ik-public";
    private const string SpkPublicCacheKey = "spk-public";
    private const string SpkSignatureCacheKey = "spk-signature";
    private const int InitialOtpkCount = 20;
    private const int ReplenishOtpkCount = 20;
    private static readonly TimeSpan SpkRotationInterval = TimeSpan.FromDays(7);

    private static readonly string[] AllKeySuffixes =
    [
        DeviceIdCacheKey,
        IkPrivateCacheKey,
        SpkPrivateCacheKey,
        OtpPrivatesCacheKey,
        OtpIdMapCacheKey,
        SpkCreatedAtCacheKey,
        IkPublicCacheKey,
        SpkPublicCacheKey,
        SpkSignatureCacheKey
    ];

    private X3DHKeyBundle? _localBundle;

    private Dictionary<string, string> _otpPrivates = new();

    private Dictionary<Guid, OtpKeyPair> _otpById = new();

    public X3DHKeyBundle LocalBundle =>
        _localBundle ?? throw new InvalidOperationException(
            "Key bundle not loaded. Call EnsureKeyBundlePublishedAsync first.");

    public OtpKeyPair? FindOtpPrivateKey(Guid opkId) =>
        _otpById.TryGetValue(opkId, out var pair) ? pair : null;

    public async Task EnsureKeyBundlePublishedAsync(CancellationToken ct = default)
    {
        if (!sessionService.IsAuthenticated)
        {
            return;
        }

        var userId = sessionService.CurrentUserId;
        if (userId == Guid.Empty)
        {
            return;
        }

        try
        {
            var scopedIk = await localCacheService.LoadAsync<string>(UserKey(userId, IkPrivateCacheKey), ct);
            if (scopedIk is not null)
            {
                await LoadBundleFromCacheAsync(userId, ct);
                await EnsureLocalBundleIntegrityAsync(userId, ct);
                await CheckSpkRotationAsync(userId, ct);
                await EnsureLocalBundleIntegrityAsync(userId, ct);
                await EnsureServerHasPublicBundleAsync(userId, ct);
                return;
            }

            var legacyIk = await localCacheService.LoadAsync<string>(LegacyKey(IkPrivateCacheKey), ct);
            if (legacyIk is not null)
            {
                try
                {
                    await LoadBundleFromCacheLegacyAsync(ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to read legacy key files; will discard and regenerate.");
                    await DeleteLegacyKeyFilesAsync(ct);
                    _localBundle = null;
                    await GenerateAndUploadBundleAsync(userId, ct);
                    return;
                }

                var remote = await apiClient.GetKeyBundleAsync(userId, ct);
                var localIk = _localBundle!.IkPublic;
                if (remote is not null && CryptographicOperations.FixedTimeEquals(remote.IkPublic, localIk))
                {
                    logger.LogInformation("Migrating legacy key cache to user-scoped files for user {UserId}.", userId);
                    await CopyLegacyCacheToUserScopedAsync(userId, ct);
                    await DeleteLegacyKeyFilesAsync(ct);
                    await EnsureServerHasPublicBundleAsync(userId, ct);
                    return;
                }

                if (remote is null)
                {
                    logger.LogInformation("Publishing legacy local keys to server (no key-bundle row yet) for user {UserId}.", userId);
                    await EnsureServerHasPublicBundleAsync(userId, ct);
                    await DeleteLegacyKeyFilesAsync(ct);
                    return;
                }

                logger.LogWarning(
                    "Discarding unscoped key cache (identity mismatch with server for user {UserId}). " +
                    "This often happens after registering a second account on the same PC.",
                    userId);
                await DeleteLegacyKeyFilesAsync(ct);
                _localBundle = null;
            }

            await GenerateAndUploadBundleAsync(userId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Key bundle publication failed — messages may use legacy encryption.");
        }
    }

    public async Task RegenerateAndPublishAsync(CancellationToken ct = default)
    {
        var userId = sessionService.CurrentUserId;
        if (userId == Guid.Empty)
        {
            throw new InvalidOperationException("Must be signed in to publish keys.");
        }

        logger.LogInformation("Regenerating identity key and re-publishing bundle.");

        _localBundle = x3dh.GenerateKeyBundle();
        var otpKeys = x3dh.GenerateOneTimePreKeys(InitialOtpkCount);

        await localCacheService.SaveAsync(UserKey(userId, IkPrivateCacheKey), keyStore.ProtectToBase64(_localBundle.IkPrivate), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkPrivateCacheKey), keyStore.ProtectToBase64(_localBundle.SpkPrivate), ct);
        await localCacheService.SaveAsync(UserKey(userId, IkPublicCacheKey), Convert.ToBase64String(_localBundle.IkPublic), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkPublicCacheKey), Convert.ToBase64String(_localBundle.SpkPublic), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkSignatureCacheKey), Convert.ToBase64String(_localBundle.SpkSignature), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkCreatedAtCacheKey), _localBundle.SpkCreatedAt.ToString("O"), ct);

        var resp = await apiClient.UploadKeyBundleAsync(
            new KeyBundleUploadRequest(
                null,
                _localBundle.IkPublic,
                _localBundle.SpkPublic,
                _localBundle.SpkSignature),
            ct);

        await localCacheService.SaveAsync(UserKey(userId, DeviceIdCacheKey), resp.DeviceId.ToString(), ct);

        var otpResp = await apiClient.ReplenishOtpksAsync(
            new OtpkReplenishRequest(resp.DeviceId, otpKeys.Select(k => k.Public).ToArray()), ct);

        _otpPrivates = otpKeys.ToDictionary(
            k => Convert.ToBase64String(k.Public),
            k => keyStore.ProtectToBase64(k.Private));
        await localCacheService.SaveAsync(UserKey(userId, OtpPrivatesCacheKey), _otpPrivates, ct);

        _otpById = new Dictionary<Guid, OtpKeyPair>();
        PopulateOtpById(otpResp.AssignedIds, otpKeys);
        await PersistOtpIdMapAsync(userId, ct);

        logger.LogInformation("Identity key regeneration complete. DeviceId={DeviceId}", resp.DeviceId);
    }

    public async Task ReplenishOtpksAsync(CancellationToken ct = default)
    {
        var userId = sessionService.CurrentUserId;
        if (userId == Guid.Empty)
        {
            return;
        }

        try
        {
            var deviceIdStr = await localCacheService.LoadAsync<string>(UserKey(userId, DeviceIdCacheKey), ct);
            if (deviceIdStr is null)
            {
                logger.LogWarning("Cannot replenish OTPs — no device ID found in cache.");
                return;
            }

            var deviceId = Guid.Parse(deviceIdStr);
            var newOtps = x3dh.GenerateOneTimePreKeys(ReplenishOtpkCount);
            var resp = await apiClient.ReplenishOtpksAsync(
                new OtpkReplenishRequest(deviceId, newOtps.Select(k => k.Public).ToArray()), ct);

            foreach (var otp in newOtps)
            {
                var pubKey = Convert.ToBase64String(otp.Public);
                _otpPrivates[pubKey] = keyStore.ProtectToBase64(otp.Private);
            }

            await localCacheService.SaveAsync(UserKey(userId, OtpPrivatesCacheKey), _otpPrivates, ct);

            PopulateOtpById(resp.AssignedIds, newOtps);
            await PersistOtpIdMapAsync(userId, ct);

            logger.LogInformation("OPK pool replenished with {Count} new keys.", ReplenishOtpkCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OPK replenishment failed.");
        }
    }

    private static string UserKey(Guid userId, string suffix) => $"{userId:N}_{suffix}";

    private static string LegacyKey(string suffix) => suffix;

    private async Task EnsureServerHasPublicBundleAsync(Guid userId, CancellationToken ct)
    {
        if (_localBundle is null)
        {
            return;
        }

        var remote = await apiClient.GetKeyBundleAsync(userId, ct);
        if (remote is not null)
        {
            return;
        }

        logger.LogWarning(
            "Server has no key bundle for user {UserId} but local keys exist — publishing a new device row.",
            userId);

        var otpKeys = x3dh.GenerateOneTimePreKeys(InitialOtpkCount);
        var resp = await apiClient.UploadKeyBundleAsync(
            new KeyBundleUploadRequest(
                null,
                _localBundle.IkPublic,
                _localBundle.SpkPublic,
                _localBundle.SpkSignature),
            ct);

        var otpResp = await apiClient.ReplenishOtpksAsync(
            new OtpkReplenishRequest(resp.DeviceId, otpKeys.Select(k => k.Public).ToArray()),
            ct);

        await localCacheService.SaveAsync(UserKey(userId, DeviceIdCacheKey), resp.DeviceId.ToString(), ct);

        _otpPrivates = otpKeys.ToDictionary(
            k => Convert.ToBase64String(k.Public),
            k => keyStore.ProtectToBase64(k.Private));
        await localCacheService.SaveAsync(UserKey(userId, OtpPrivatesCacheKey), _otpPrivates, ct);

        _otpById = new Dictionary<Guid, OtpKeyPair>();
        PopulateOtpById(otpResp.AssignedIds, otpKeys);
        await PersistOtpIdMapAsync(userId, ct);
        await PersistLocalBundleSecretsToScopedAsync(userId, ct);

        logger.LogInformation("Re-published key bundle to server. DeviceId={DeviceId}", resp.DeviceId);
    }

    private async Task PersistLocalBundleSecretsToScopedAsync(Guid userId, CancellationToken ct)
    {
        if (_localBundle is null)
        {
            throw new InvalidOperationException("Local bundle is not loaded.");
        }

        await localCacheService.SaveAsync(UserKey(userId, IkPrivateCacheKey), keyStore.ProtectToBase64(_localBundle.IkPrivate), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkPrivateCacheKey), keyStore.ProtectToBase64(_localBundle.SpkPrivate), ct);
        await localCacheService.SaveAsync(UserKey(userId, IkPublicCacheKey), Convert.ToBase64String(_localBundle.IkPublic), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkPublicCacheKey), Convert.ToBase64String(_localBundle.SpkPublic), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkSignatureCacheKey), Convert.ToBase64String(_localBundle.SpkSignature), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkCreatedAtCacheKey), _localBundle.SpkCreatedAt.ToString("O"), ct);
    }

    private async Task CopyLegacyCacheToUserScopedAsync(Guid userId, CancellationToken ct)
    {
        await CopyIfPresentAsync<string>(userId, DeviceIdCacheKey, ct);
        await CopyIfPresentAsync<string>(userId, IkPrivateCacheKey, ct);
        await CopyIfPresentAsync<string>(userId, SpkPrivateCacheKey, ct);
        await CopyIfPresentAsync<string>(userId, IkPublicCacheKey, ct);
        await CopyIfPresentAsync<string>(userId, SpkPublicCacheKey, ct);
        await CopyIfPresentAsync<string>(userId, SpkSignatureCacheKey, ct);
        await CopyIfPresentAsync<string>(userId, SpkCreatedAtCacheKey, ct);
        await CopyIfPresentAsync<Dictionary<string, string>>(userId, OtpPrivatesCacheKey, ct);
        await CopyIfPresentAsync<Dictionary<string, string>>(userId, OtpIdMapCacheKey, ct);
    }

    private async Task CopyIfPresentAsync<T>(Guid userId, string suffix, CancellationToken ct) where T : class
    {
        var data = await localCacheService.LoadAsync<T>(LegacyKey(suffix), ct);
        if (data is not null)
        {
            await localCacheService.SaveAsync(UserKey(userId, suffix), data, ct);
        }
    }

    private async Task DeleteLegacyKeyFilesAsync(CancellationToken ct)
    {
        foreach (var suffix in AllKeySuffixes)
        {
            await localCacheService.DeleteAsync(LegacyKey(suffix), ct);
        }
    }

    private async Task GenerateAndUploadBundleAsync(Guid userId, CancellationToken ct)
    {
        logger.LogInformation("No local key bundle found — generating new bundle.");

        _localBundle = x3dh.GenerateKeyBundle();
        var otpKeys = x3dh.GenerateOneTimePreKeys(InitialOtpkCount);

        var resp = await apiClient.UploadKeyBundleAsync(
            new KeyBundleUploadRequest(
                null,
                _localBundle.IkPublic,
                _localBundle.SpkPublic,
                _localBundle.SpkSignature),
            ct);

        var otpResp = await apiClient.ReplenishOtpksAsync(
            new OtpkReplenishRequest(resp.DeviceId, otpKeys.Select(k => k.Public).ToArray()),
            ct);

        await localCacheService.SaveAsync(UserKey(userId, DeviceIdCacheKey), resp.DeviceId.ToString(), ct);

        _otpPrivates = otpKeys.ToDictionary(
            k => Convert.ToBase64String(k.Public),
            k => keyStore.ProtectToBase64(k.Private));
        await localCacheService.SaveAsync(UserKey(userId, OtpPrivatesCacheKey), _otpPrivates, ct);

        PopulateOtpById(otpResp.AssignedIds, otpKeys);
        await PersistOtpIdMapAsync(userId, ct);
        await PersistLocalBundleSecretsToScopedAsync(userId, ct);

        logger.LogInformation("Key bundle published. DeviceId={DeviceId}", resp.DeviceId);
    }

    private async Task LoadBundleFromCacheAsync(Guid userId, CancellationToken ct) =>
        await LoadBundleFromCacheCoreAsync(
            suffix => UserKey(userId, suffix),
            ct);

    private async Task LoadBundleFromCacheLegacyAsync(CancellationToken ct) =>
        await LoadBundleFromCacheCoreAsync(LegacyKey, ct);

    private async Task LoadBundleFromCacheCoreAsync(Func<string, string> key, CancellationToken ct)
    {
        var ikPrivDpapi = await localCacheService.LoadAsync<string>(key(IkPrivateCacheKey), ct)
                          ?? throw new InvalidOperationException("IK private key missing from cache.");
        var spkPrivDpapi = await localCacheService.LoadAsync<string>(key(SpkPrivateCacheKey), ct)
                           ?? throw new InvalidOperationException("SPK private key missing from cache.");
        var ikPubB64 = await localCacheService.LoadAsync<string>(key(IkPublicCacheKey), ct)
                       ?? throw new InvalidOperationException("IK public key missing from cache.");
        var spkPubB64 = await localCacheService.LoadAsync<string>(key(SpkPublicCacheKey), ct)
                        ?? throw new InvalidOperationException("SPK public key missing from cache.");
        var spkSigB64 = await localCacheService.LoadAsync<string>(key(SpkSignatureCacheKey), ct)
                        ?? throw new InvalidOperationException("SPK signature missing from cache.");
        var spkCreatedAtStr = await localCacheService.LoadAsync<string>(key(SpkCreatedAtCacheKey), ct)
                              ?? throw new InvalidOperationException("SPK creation time missing from cache.");

        _localBundle = new X3DHKeyBundle(
            IkPublic: Convert.FromBase64String(ikPubB64),
            IkPrivate: keyStore.UnprotectFromBase64(ikPrivDpapi),
            SpkPublic: Convert.FromBase64String(spkPubB64),
            SpkPrivate: keyStore.UnprotectFromBase64(spkPrivDpapi),
            SpkSignature: Convert.FromBase64String(spkSigB64),
            SpkCreatedAt: DateTimeOffset.Parse(spkCreatedAtStr));

        _otpPrivates = await localCacheService.LoadAsync<Dictionary<string, string>>(key(OtpPrivatesCacheKey), ct)
                       ?? new Dictionary<string, string>();

        _otpById = new Dictionary<Guid, OtpKeyPair>();
        var otpIdMap = await localCacheService.LoadAsync<Dictionary<string, string>>(key(OtpIdMapCacheKey), ct);
        if (otpIdMap is not null)
        {
            foreach (var (guidStr, pubKeyB64) in otpIdMap)
            {
                if (Guid.TryParse(guidStr, out var opkId) && _otpPrivates.TryGetValue(pubKeyB64, out var privDpapi))
                {
                    _otpById[opkId] = new OtpKeyPair(
                        Convert.FromBase64String(pubKeyB64),
                        keyStore.UnprotectFromBase64(privDpapi));
                }
            }
        }

        logger.LogDebug("Local key bundle loaded from cache. SPK created at {SpkCreatedAt}.", _localBundle.SpkCreatedAt);
    }

    private void PopulateOtpById(Guid[] assignedIds, IReadOnlyList<OtpKeyPair> otpKeys)
    {
        for (var i = 0; i < Math.Min(assignedIds.Length, otpKeys.Count); i++)
        {
            _otpById[assignedIds[i]] = otpKeys[i];
        }
    }

    private async Task PersistOtpIdMapAsync(Guid userId, CancellationToken ct)
    {
        var map = _otpById.ToDictionary(
            kvp => kvp.Key.ToString(),
            kvp => Convert.ToBase64String(kvp.Value.Public));
        await localCacheService.SaveAsync(UserKey(userId, OtpIdMapCacheKey), map, ct);
    }

    private async Task CheckSpkRotationAsync(Guid userId, CancellationToken ct)
    {
        if (_localBundle is null) return;

        if (DateTimeOffset.UtcNow - _localBundle.SpkCreatedAt <= SpkRotationInterval)
        {
            return;
        }

        logger.LogInformation("SPK is older than 7 days — rotating.");

        var deviceIdStr = await localCacheService.LoadAsync<string>(UserKey(userId, DeviceIdCacheKey), ct);
        if (deviceIdStr is null)
        {
            logger.LogWarning("Cannot rotate SPK — no device ID found in cache.");
            return;
        }

        var deviceId = Guid.Parse(deviceIdStr);

        var newBundle = x3dh.GenerateKeyBundle();

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

        _localBundle = rotated;
        await localCacheService.SaveAsync(UserKey(userId, SpkPrivateCacheKey), keyStore.ProtectToBase64(rotated.SpkPrivate), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkPublicCacheKey), Convert.ToBase64String(rotated.SpkPublic), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkSignatureCacheKey), Convert.ToBase64String(rotated.SpkSignature), ct);
        await localCacheService.SaveAsync(UserKey(userId, SpkCreatedAtCacheKey), rotated.SpkCreatedAt.ToString("O"), ct);

        logger.LogInformation("SPK rotation complete.");
    }

    private async Task EnsureLocalBundleIntegrityAsync(Guid userId, CancellationToken ct)
    {
        if (_localBundle is null)
        {
            return;
        }

        if (IsSpkPairConsistent(_localBundle))
        {
            return;
        }

        logger.LogWarning("Local SPK private/public mismatch detected. Regenerating and re-publishing key bundle.");
        await RegenerateAndPublishAsync(ct);
    }

    private static bool IsSpkPairConsistent(X3DHKeyBundle bundle)
    {
        try
        {
            using var imported = Key.Import(KeyAgreementAlgorithm.X25519, bundle.SpkPrivate, KeyBlobFormat.RawPrivateKey);
            var derivedPub = imported.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            return CryptographicOperations.FixedTimeEquals(derivedPub, bundle.SpkPublic);
        }
        catch
        {
            return false;
        }
    }
}
