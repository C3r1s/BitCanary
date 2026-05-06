// Серверная часть связок ключей: валидация SPK и батч одноразовых pre-keys.
using System.Net;
using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Application.Keys;

public sealed class KeyBundleService(
    IAppDbContext dbContext,
    ISpkValidator spkValidator,
    IRealtimeNotifier realtimeNotifier,
    ILogger<KeyBundleService> logger) : IKeyBundleService
{
    private const int MaxOpkBatchSize = 100;
    private const int OpkLowThreshold = 10;

    public async Task<BundleUploadResponse> UploadBundleAsync(Guid userId, KeyBundleUploadRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "KeyBundle upload requested. user={UserId} device={DeviceId} hasDevice={HasDevice}",
            userId, request.DeviceId, request.DeviceId.HasValue);

        if (!spkValidator.Validate(request.IkPublic, request.SpkPublic, request.SpkSignature))
        {
            logger.LogWarning("KeyBundle upload rejected: invalid SPK signature. user={UserId}", userId);
            throw new AppException("SPK signature verification failed.", HttpStatusCode.BadRequest);
        }

        if (request.DeviceId.HasValue)
        {
            var existing = await dbContext.UserKeyBundles
                .SingleOrDefaultAsync(x => x.UserId == userId && x.DeviceId == request.DeviceId.Value, cancellationToken);

            if (existing is null)
            {
                logger.LogWarning(
                    "KeyBundle rotation failed: bundle not found. user={UserId} device={DeviceId}",
                    userId, request.DeviceId.Value);
                throw new AppException("Bundle not found for the specified device.", HttpStatusCode.NotFound);
            }

            existing.IkPublic = request.IkPublic;
            existing.SpkPublic = request.SpkPublic;
            existing.SpkSignature = request.SpkSignature;
            existing.SpkCreatedAt = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "KeyBundle rotated successfully. user={UserId} device={DeviceId}",
                userId, existing.DeviceId);
            return new BundleUploadResponse(existing.DeviceId);
        }

        var staleOpks = await dbContext.OneTimePreKeys
            .Where(x => x.UserId == userId && x.ClaimedAt == null)
            .ToListAsync(cancellationToken);
        if (staleOpks.Count > 0)
        {
            dbContext.OneTimePreKeys.RemoveRange(staleOpks);
            logger.LogInformation(
                "Removed stale unclaimed OPKs before new bundle publish. user={UserId} removed={RemovedCount}",
                userId, staleOpks.Count);
        }

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
        logger.LogInformation(
            "KeyBundle created for new device. user={UserId} device={DeviceId}",
            userId, deviceId);
        return new BundleUploadResponse(deviceId);
    }

    public async Task<KeyBundleDto?> GetBundleAsync(Guid userId, CancellationToken cancellationToken)
    {
        logger.LogInformation("KeyBundle fetch requested. user={UserId}", userId);
        var bundle = await dbContext.UserKeyBundles
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.SpkCreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (bundle is null)
        {
            logger.LogInformation("KeyBundle not found. user={UserId}", userId);
            return null;
        }

        var countBefore = await dbContext.OneTimePreKeys
            .CountAsync(x => x.UserId == userId && x.ClaimedAt == null, cancellationToken);
        logger.LogInformation(
            "OPK claim start. user={UserId} countBefore={CountBefore} bundleDevice={DeviceId}",
            userId, countBefore, bundle.DeviceId);

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
                logger.LogInformation(
                    "OPK claimed (relational). user={UserId} opkId={OpkId}",
                    userId, opk.Id);
            }
            await tx.CommitAsync(cancellationToken);
        }
        else
        {
            opk = await dbContext.OneTimePreKeys
                .Where(x => x.UserId == userId && x.ClaimedAt == null)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (opk is not null)
            {
                opk.ClaimedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "OPK claimed (in-memory). user={UserId} opkId={OpkId}",
                    userId, opk.Id);
            }
        }

        var countAfter = countBefore > 0 ? countBefore - 1 : 0;
        if (opk is not null && countBefore >= OpkLowThreshold && countAfter < OpkLowThreshold)
        {
            await realtimeNotifier.SendOtpkSupplyLowAsync(userId, cancellationToken);
            logger.LogWarning(
                "OPK supply low notification sent. user={UserId} threshold={Threshold} countAfter={CountAfter}",
                userId, OpkLowThreshold, countAfter);
        }

        logger.LogInformation(
            "KeyBundle fetch result. user={UserId} device={DeviceId} opkReturned={HasOpk} countAfter={CountAfter}",
            userId, bundle.DeviceId, opk is not null, countAfter);

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

    public async Task<OtpkReplenishResponse> ReplenishOpksAsync(Guid userId, OtpkReplenishRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "OPK replenish requested. user={UserId} device={DeviceId} count={Count}",
            userId, request.DeviceId, request.PreKeys.Length);

        if (request.PreKeys.Length > MaxOpkBatchSize)
        {
            logger.LogWarning(
                "OPK replenish rejected: batch too large. user={UserId} count={Count} max={Max}",
                userId, request.PreKeys.Length, MaxOpkBatchSize);
            throw new AppException($"OPK batch size exceeds maximum of {MaxOpkBatchSize}.", HttpStatusCode.BadRequest);
        }

        var bundleExists = await dbContext.UserKeyBundles
            .AnyAsync(x => x.UserId == userId && x.DeviceId == request.DeviceId, cancellationToken);

        if (!bundleExists)
        {
            logger.LogWarning(
                "OPK replenish rejected: bundle not found. user={UserId} device={DeviceId}",
                userId, request.DeviceId);
            throw new AppException("No bundle found for the specified device.", HttpStatusCode.NotFound);
        }

        var latestDeviceId = await dbContext.UserKeyBundles
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.SpkCreatedAt)
            .Select(x => x.DeviceId)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestDeviceId != request.DeviceId)
        {
            logger.LogWarning(
                "OPK replenish rejected: device is not latest. user={UserId} requested={RequestedDevice} latest={LatestDevice}",
                userId, request.DeviceId, latestDeviceId);
            throw new AppException("OPK replenishment is allowed only for the latest active device bundle.", HttpStatusCode.Conflict);
        }

        var entities = new List<OneTimePreKey>(request.PreKeys.Length);
        foreach (var preKey in request.PreKeys)
        {
            var entity = new OneTimePreKey
            {
                UserId = userId,
                PublicKey = preKey
            };
            entities.Add(entity);
            dbContext.OneTimePreKeys.Add(entity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var assignedIds = entities.Select(e => e.Id).ToArray();
        logger.LogInformation(
            "OPK replenish succeeded. user={UserId} device={DeviceId} assigned={AssignedCount}",
            userId, request.DeviceId, assignedIds.Length);
        return new OtpkReplenishResponse(assignedIds);
    }
}
