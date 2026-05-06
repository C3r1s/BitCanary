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
                await EnsureLocalBundleIntegrityAsync(ct);
                await CheckSpkRotationAsync(ct);
                await EnsureLocalBundleIntegrityAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Key bundle publication failed — messages may use legacy encryption.");
        }
    }

    public async Task RegenerateAndPublishAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Regenerating identity key and re-publishing bundle.");

        _localBundle = x3dh.GenerateKeyBundle();
        var otpKeys = x3dh.GenerateOneTimePreKeys(InitialOtpkCount);

        await localCacheService.SaveAsync(IkPrivateCacheKey, keyStore.ProtectToBase64(_localBundle.IkPrivate), ct);
        await localCacheService.SaveAsync(SpkPrivateCacheKey, keyStore.ProtectToBase64(_localBundle.SpkPrivate), ct);
        await localCacheService.SaveAsync(IkPublicCacheKey, Convert.ToBase64String(_localBundle.IkPublic), ct);
        await localCacheService.SaveAsync(SpkPublicCacheKey, Convert.ToBase64String(_localBundle.SpkPublic), ct);
        await localCacheService.SaveAsync(SpkSignatureCacheKey, Convert.ToBase64String(_localBundle.SpkSignature), ct);
        await localCacheService.SaveAsync(SpkCreatedAtCacheKey, _localBundle.SpkCreatedAt.ToString("O"), ct);

        var resp = await apiClient.UploadKeyBundleAsync(
            new KeyBundleUploadRequest(
                null,
                _localBundle.IkPublic,
                _localBundle.SpkPublic,
                _localBundle.SpkSignature),
            ct);

        await localCacheService.SaveAsync(DeviceIdCacheKey, resp.DeviceId.ToString(), ct);

        var otpResp = await apiClient.ReplenishOtpksAsync(
            new OtpkReplenishRequest(resp.DeviceId, otpKeys.Select(k => k.Public).ToArray()), ct);

        _otpPrivates = otpKeys.ToDictionary(
            k => Convert.ToBase64String(k.Public),
            k => keyStore.ProtectToBase64(k.Private));
        await localCacheService.SaveAsync(OtpPrivatesCacheKey, _otpPrivates, ct);

        _otpById = new Dictionary<Guid, OtpKeyPair>();
        PopulateOtpById(otpResp.AssignedIds, otpKeys);
        await PersistOtpIdMapAsync(ct);

        logger.LogInformation("Identity key regeneration complete. DeviceId={DeviceId}", resp.DeviceId);
    }

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
            var resp = await apiClient.ReplenishOtpksAsync(
                new OtpkReplenishRequest(deviceId, newOtps.Select(k => k.Public).ToArray()), ct);

            foreach (var otp in newOtps)
            {
                var pubKey = Convert.ToBase64String(otp.Public);
                _otpPrivates[pubKey] = keyStore.ProtectToBase64(otp.Private);
            }

            await localCacheService.SaveAsync(OtpPrivatesCacheKey, _otpPrivates, ct);

            PopulateOtpById(resp.AssignedIds, newOtps);
            await PersistOtpIdMapAsync(ct);

            logger.LogInformation("OPK pool replenished with {Count} new keys.", ReplenishOtpkCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OPK replenishment failed.");
        }
    }


    private async Task GenerateAndUploadBundleAsync(CancellationToken ct)
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

        await localCacheService.SaveAsync(DeviceIdCacheKey, resp.DeviceId.ToString(), ct);

        await localCacheService.SaveAsync(IkPrivateCacheKey, keyStore.ProtectToBase64(_localBundle.IkPrivate), ct);
        await localCacheService.SaveAsync(SpkPrivateCacheKey, keyStore.ProtectToBase64(_localBundle.SpkPrivate), ct);
        await localCacheService.SaveAsync(IkPublicCacheKey, Convert.ToBase64String(_localBundle.IkPublic), ct);
        await localCacheService.SaveAsync(SpkPublicCacheKey, Convert.ToBase64String(_localBundle.SpkPublic), ct);
        await localCacheService.SaveAsync(SpkSignatureCacheKey, Convert.ToBase64String(_localBundle.SpkSignature), ct);
        await localCacheService.SaveAsync(SpkCreatedAtCacheKey, _localBundle.SpkCreatedAt.ToString("O"), ct);

        _otpPrivates = otpKeys.ToDictionary(
            k => Convert.ToBase64String(k.Public),
            k => keyStore.ProtectToBase64(k.Private));
        await localCacheService.SaveAsync(OtpPrivatesCacheKey, _otpPrivates, ct);

        PopulateOtpById(otpResp.AssignedIds, otpKeys);
        await PersistOtpIdMapAsync(ct);

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

        _otpById = new Dictionary<Guid, OtpKeyPair>();
        var otpIdMap = await localCacheService.LoadAsync<Dictionary<string, string>>(OtpIdMapCacheKey, ct);
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

    private async Task PersistOtpIdMapAsync(CancellationToken ct)
    {
        var map = _otpById.ToDictionary(
            kvp => kvp.Key.ToString(),
            kvp => Convert.ToBase64String(kvp.Value.Public));
        await localCacheService.SaveAsync(OtpIdMapCacheKey, map, ct);
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
        await localCacheService.SaveAsync(SpkPrivateCacheKey, keyStore.ProtectToBase64(rotated.SpkPrivate), ct);
        await localCacheService.SaveAsync(SpkPublicCacheKey, Convert.ToBase64String(rotated.SpkPublic), ct);
        await localCacheService.SaveAsync(SpkSignatureCacheKey, Convert.ToBase64String(rotated.SpkSignature), ct);
        await localCacheService.SaveAsync(SpkCreatedAtCacheKey, rotated.SpkCreatedAt.ToString("O"), ct);

        logger.LogInformation("SPK rotation complete.");
    }

    private async Task EnsureLocalBundleIntegrityAsync(CancellationToken ct)
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
