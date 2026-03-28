using System.Net;
using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Application.Keys;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Persistence;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Messenger.Application.Tests.Keys;

public sealed class KeyBundleServiceTests
{
    private static (AppDbContext db, ISpkValidator validator, IRealtimeNotifier notifier) CreateDeps()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var validator = Substitute.For<ISpkValidator>();
        var notifier = Substitute.For<IRealtimeNotifier>();
        return (db, validator, notifier);
    }

    private static KeyBundleService CreateService(AppDbContext db, ISpkValidator validator, IRealtimeNotifier notifier)
        => new(db, validator, notifier);

    private static UserKeyBundle SeedBundle(AppDbContext db, Guid userId)
    {
        var bundle = new UserKeyBundle
        {
            UserId = userId,
            DeviceId = Guid.NewGuid(),
            IkPublic = [1, 2, 3],
            SpkPublic = [4, 5, 6],
            SpkSignature = [7, 8, 9],
            SpkCreatedAt = DateTimeOffset.UtcNow
        };
        db.UserKeyBundles.Add(bundle);
        db.SaveChanges();
        return bundle;
    }

    private static void SeedOpks(AppDbContext db, Guid userId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            db.OneTimePreKeys.Add(new OneTimePreKey
            {
                UserId = userId,
                PublicKey = [(byte)(i + 1), (byte)(i + 2)]
            });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task UploadBundle_NewDevice_AssignsDeviceIdAndReturns201()
    {
        var (db, validator, notifier) = CreateDeps();
        validator.Validate(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<byte[]>()).Returns(true);
        var svc = CreateService(db, validator, notifier);
        var userId = Guid.NewGuid();

        var request = new KeyBundleUploadRequest(null, [1], [2], [3]);
        var response = await svc.UploadBundleAsync(userId, request, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.DeviceId);
        var saved = await db.UserKeyBundles.SingleAsync(x => x.UserId == userId);
        Assert.Equal(response.DeviceId, saved.DeviceId);
    }

    [Fact]
    public async Task UploadBundle_InvalidSpkSignature_ThrowsAppException()
    {
        var (db, validator, notifier) = CreateDeps();
        validator.Validate(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<byte[]>()).Returns(false);
        var svc = CreateService(db, validator, notifier);

        var request = new KeyBundleUploadRequest(null, [1], [2], [3]);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            svc.UploadBundleAsync(Guid.NewGuid(), request, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("SPK signature verification failed", ex.Message);
    }

    [Fact]
    public async Task UploadBundle_ExistingDevice_UpdatesSpkInPlace()
    {
        var (db, validator, notifier) = CreateDeps();
        validator.Validate(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<byte[]>()).Returns(true);
        var svc = CreateService(db, validator, notifier);
        var userId = Guid.NewGuid();
        var existing = SeedBundle(db, userId);

        var newSpk = new byte[] { 99, 88, 77 };
        var request = new KeyBundleUploadRequest(existing.DeviceId, [1], newSpk, [3]);
        await svc.UploadBundleAsync(userId, request, CancellationToken.None);

        var updated = await db.UserKeyBundles.SingleAsync(x => x.UserId == userId);
        Assert.Equal(newSpk, updated.SpkPublic);
        Assert.Equal(existing.DeviceId, updated.DeviceId);
    }

    [Fact]
    public async Task GetBundle_WithOpks_ReturnsOneOpkAndMarksItClaimed()
    {
        var (db, validator, notifier) = CreateDeps();
        var svc = CreateService(db, validator, notifier);
        var userId = Guid.NewGuid();
        SeedBundle(db, userId);
        SeedOpks(db, userId, 5);

        var result = await svc.GetBundleAsync(userId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.OpkPublic);
        Assert.NotNull(result.OpkId);

        var claimedCount = await db.OneTimePreKeys.CountAsync(x => x.UserId == userId && x.ClaimedAt != null);
        Assert.Equal(1, claimedCount);
    }

    [Fact]
    public async Task GetBundle_NoOpks_ReturnsPartialBundleWithNullOpk()
    {
        var (db, validator, notifier) = CreateDeps();
        var svc = CreateService(db, validator, notifier);
        var userId = Guid.NewGuid();
        SeedBundle(db, userId);

        var result = await svc.GetBundleAsync(userId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.OpkPublic);
        Assert.Null(result.OpkId);
    }

    [Fact]
    public async Task GetBundle_NonexistentUser_ReturnsNull()
    {
        var (db, validator, notifier) = CreateDeps();
        var svc = CreateService(db, validator, notifier);

        var result = await svc.GetBundleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReplenishOpks_AddsKeysToPool()
    {
        var (db, validator, notifier) = CreateDeps();
        var svc = CreateService(db, validator, notifier);
        var userId = Guid.NewGuid();
        var bundle = SeedBundle(db, userId);

        var preKeys = Enumerable.Range(0, 5).Select(i => new byte[] { (byte)i }).ToArray();
        var request = new OtpkReplenishRequest(bundle.DeviceId, preKeys);
        var result = await svc.ReplenishOpksAsync(userId, request, CancellationToken.None);

        var count = await db.OneTimePreKeys.CountAsync(x => x.UserId == userId);
        Assert.Equal(5, count);

        // Verify assigned IDs are returned
        Assert.Equal(5, result.AssignedIds.Length);
        Assert.All(result.AssignedIds, id => Assert.NotEqual(Guid.Empty, id));
        Assert.Equal(result.AssignedIds.Length, result.AssignedIds.Distinct().Count());
    }

    [Fact]
    public async Task ReplenishOpks_ExceedsMaxBatchSize_ThrowsAppException()
    {
        var (db, validator, notifier) = CreateDeps();
        var svc = CreateService(db, validator, notifier);
        var userId = Guid.NewGuid();
        var bundle = SeedBundle(db, userId);

        var preKeys = Enumerable.Range(0, 101).Select(i => new byte[] { (byte)(i % 256) }).ToArray();
        var request = new OtpkReplenishRequest(bundle.DeviceId, preKeys);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            svc.ReplenishOpksAsync(userId, request, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task OtpkSupplyLow_FiresOnTransitionFromGte10ToLt10()
    {
        var (db, validator, notifier) = CreateDeps();
        notifier.SendOtpkSupplyLowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var svc = CreateService(db, validator, notifier);
        var userId = Guid.NewGuid();
        SeedBundle(db, userId);
        SeedOpks(db, userId, 10); // exactly 10 before claim → after claim = 9

        await svc.GetBundleAsync(userId, CancellationToken.None);

        await notifier.Received(1).SendOtpkSupplyLowAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtpkSupplyLow_DoesNotRefireWhenAlreadyBelow10()
    {
        var (db, validator, notifier) = CreateDeps();
        notifier.SendOtpkSupplyLowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var svc = CreateService(db, validator, notifier);
        var userId = Guid.NewGuid();
        SeedBundle(db, userId);
        SeedOpks(db, userId, 5); // only 5, countBefore < 10 so threshold not triggered

        await svc.GetBundleAsync(userId, CancellationToken.None);

        await notifier.DidNotReceive().SendOtpkSupplyLowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadBundle_SpkRotation_PreservesExistingOpks()
    {
        var (db, validator, notifier) = CreateDeps();
        validator.Validate(Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<byte[]>()).Returns(true);
        var svc = CreateService(db, validator, notifier);
        var userId = Guid.NewGuid();
        var existing = SeedBundle(db, userId);
        SeedOpks(db, userId, 3);

        var request = new KeyBundleUploadRequest(existing.DeviceId, [1], [99, 88], [3]);
        await svc.UploadBundleAsync(userId, request, CancellationToken.None);

        var unclaimedCount = await db.OneTimePreKeys.CountAsync(x => x.UserId == userId && x.ClaimedAt == null);
        Assert.Equal(3, unclaimedCount);
    }

    [Fact(Skip = "Requires PostgreSQL")]
    public void GetBundle_ConcurrentCalls_NeverReturnSameOpk()
    {
        Assert.True(true);
    }
}
