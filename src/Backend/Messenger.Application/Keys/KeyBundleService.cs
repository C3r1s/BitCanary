using System.Net;
using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Application.Keys;

public sealed class KeyBundleService(
    IAppDbContext dbContext,
    ISpkValidator spkValidator,
    IRealtimeNotifier realtimeNotifier) : IKeyBundleService
{
    private const int MaxOpkBatchSize = 100;
    private const int OpkLowThreshold = 10;

    public async Task<BundleUploadResponse> UploadBundleAsync(Guid userId, KeyBundleUploadRequest request, CancellationToken cancellationToken)
    {
        if (!spkValidator.Validate(request.IkPublic, request.SpkPublic, request.SpkSignature))
            throw new AppException("SPK signature verification failed.", HttpStatusCode.BadRequest);

        if (request.DeviceId.HasValue)
        {
            // Rotation (D-05): in-place UPDATE
            var existing = await dbContext.UserKeyBundles
                .SingleOrDefaultAsync(x => x.UserId == userId && x.DeviceId == request.DeviceId.Value, cancellationToken);

            if (existing is null)
                throw new AppException("Bundle not found for the specified device.", HttpStatusCode.NotFound);

            existing.IkPublic = request.IkPublic;
            existing.SpkPublic = request.SpkPublic;
            existing.SpkSignature = request.SpkSignature;
            existing.SpkCreatedAt = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            return new BundleUploadResponse(existing.DeviceId);
        }

        // New device (D-03): server assigns device_id
        var deviceId = Guid.NewGuid();
        var bundle = new UserKeyBundle
        {
            UserId = userId,
            DeviceId = deviceId,
            IkPublic = request.IkPublic,
            SpkPublic = request.SpkPublic,
            SpkSignature = request.SpkSignature,
            SpkCreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.UserKeyBundles.Add(bundle);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new BundleUploadResponse(deviceId);
    }

    public async Task<KeyBundleDto?> GetBundleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var bundle = await dbContext.UserKeyBundles
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (bundle is null)
            return null;

        // Count before claim (D-07 threshold logic)
        var countBefore = await dbContext.OneTimePreKeys
            .CountAsync(x => x.UserId == userId && x.ClaimedAt == null, cancellationToken);

        // Atomic OPK claim
        OneTimePreKey? opk;
        var db = (dbContext as DbContext)!;
        if (db.Database.IsRelational())
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
            opk = await dbContext.OneTimePreKeys
                .FromSqlInterpolated($"SELECT * FROM one_time_pre_keys WHERE user_id = {userId} AND claimed_at IS NULL LIMIT 1 FOR UPDATE SKIP LOCKED")
                .AsTracking()
                .FirstOrDefaultAsync(cancellationToken);
            if (opk is not null)
            {
                opk.ClaimedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            await tx.CommitAsync(cancellationToken);
        }
        else
        {
            // InMemory fallback for tests
            opk = await dbContext.OneTimePreKeys
                .Where(x => x.UserId == userId && x.ClaimedAt == null)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (opk is not null)
            {
                opk.ClaimedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // OtpkSupplyLow threshold (D-07)
        var countAfter = countBefore > 0 ? countBefore - 1 : 0;
        if (opk is not null && countBefore >= OpkLowThreshold && countAfter < OpkLowThreshold)
        {
            await realtimeNotifier.SendOtpkSupplyLowAsync(userId, cancellationToken);
        }

        return new KeyBundleDto(
            bundle.UserId,
            bundle.DeviceId,
            bundle.IkPublic,
            bundle.SpkPublic,
            bundle.SpkSignature,
            bundle.SpkCreatedAt,
            opk?.PublicKey,
            opk?.Id);
    }

    public async Task ReplenishOpksAsync(Guid userId, OtpkReplenishRequest request, CancellationToken cancellationToken)
    {
        if (request.PreKeys.Length > MaxOpkBatchSize)
            throw new AppException($"OPK batch size exceeds maximum of {MaxOpkBatchSize}.", HttpStatusCode.BadRequest);

        var bundleExists = await dbContext.UserKeyBundles
            .AnyAsync(x => x.UserId == userId && x.DeviceId == request.DeviceId, cancellationToken);

        if (!bundleExists)
            throw new AppException("No bundle found for the specified device.", HttpStatusCode.NotFound);

        foreach (var preKey in request.PreKeys)
        {
            dbContext.OneTimePreKeys.Add(new OneTimePreKey
            {
                UserId = userId,
                PublicKey = preKey
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
