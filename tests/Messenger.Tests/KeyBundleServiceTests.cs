// Автотест BitCanary: проверка «KeyBundleServiceTests».
using FluentAssertions;
using Messenger.Application.Abstractions;
using Messenger.Application.Keys;
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts.Dtos;
using Messenger.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Messenger.Tests;

public sealed class KeyBundleServiceTests
{
    private static (KeyBundleService sut, IAppDbContext db, FakeSpkValidator spk, IRealtimeNotifier notifier) CreateDeps()
    {
        var db = FakeDbContextFactory.Create();
        var spk = new FakeSpkValidator { Result = true };
        var notifier = FakeRealtimeNotifier.Create();
        return (new KeyBundleService(db, spk, notifier, NullLogger<KeyBundleService>.Instance), db, spk, notifier);
    }

    [Fact]
    public async Task UploadBundle_NewDevice_SavesAndReturnsDeviceId()
    {
        var (sut, db, _, _) = CreateDeps();
        var userId = Guid.NewGuid();
        var (ikPublic, spkPublic, spkSignature) = TestKeys.GenerateSignedSpkBundle();
        var request = new KeyBundleUploadRequest(
            DeviceId: null,
            IkPublic: ikPublic,
            SpkPublic: spkPublic,
            SpkSignature: spkSignature);

        var response = await sut.UploadBundleAsync(userId, request, CancellationToken.None);

        response.DeviceId.Should().NotBe(Guid.Empty);

        var persisted = db.UserKeyBundles.Single();
        persisted.UserId.Should().Be(userId);
        persisted.DeviceId.Should().Be(response.DeviceId);
        persisted.IkPublic.Should().BeEquivalentTo(ikPublic);
        persisted.SpkPublic.Should().BeEquivalentTo(spkPublic);
        persisted.SpkSignature.Should().BeEquivalentTo(spkSignature);
    }

    [Fact]
    public async Task GetBundle_AfterUpload_ReturnsStoredBundle()
    {
        var (sut, _, _, _) = CreateDeps();
        var userId = Guid.NewGuid();
        var (ikPublic, spkPublic, spkSignature) = TestKeys.GenerateSignedSpkBundle();
        await sut.UploadBundleAsync(
            userId,
            new KeyBundleUploadRequest(null, ikPublic, spkPublic, spkSignature),
            CancellationToken.None);

        var result = await sut.GetBundleAsync(userId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(userId);
        result.IkPublic.Should().BeEquivalentTo(ikPublic);
        result.SpkPublic.Should().BeEquivalentTo(spkPublic);
        result.SpkSignature.Should().BeEquivalentTo(spkSignature);
    }

    [Fact]
    public async Task GetBundle_WithUnclaimedOpk_AtomicallyClaims()
    {
        var (sut, db, _, _) = CreateDeps();
        var userId = Guid.NewGuid();
        var bundle = new UserKeyBundle
        {
            UserId = userId,
            DeviceId = Guid.NewGuid(),
            IkPublic = new byte[32],
            SpkPublic = new byte[32],
            SpkSignature = new byte[64],
            SpkCreatedAt = DateTimeOffset.UtcNow
        };
        var opkBytes = new byte[32];
        Random.Shared.NextBytes(opkBytes);
        var opk = new OneTimePreKey
        {
            UserId = userId,
            PublicKey = opkBytes
        };
        db.UserKeyBundles.Add(bundle);
        db.OneTimePreKeys.Add(opk);
        await db.SaveChangesAsync();

        var result = await sut.GetBundleAsync(userId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.OpkPublic.Should().NotBeNull();
        result.OpkPublic!.Should().BeEquivalentTo(opkBytes);
        result.OpkId.Should().Be(opk.Id);

        var refreshed = db.OneTimePreKeys.Single(x => x.Id == opk.Id);
        refreshed.ClaimedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetBundle_TwoBundlesExist_ReturnsNewest()
    {
        var (sut, db, _, _) = CreateDeps();
        var userId = Guid.NewGuid();
        var olderDeviceId = Guid.NewGuid();
        var newerDeviceId = Guid.NewGuid();
        var older = new UserKeyBundle
        {
            UserId = userId,
            DeviceId = olderDeviceId,
            IkPublic = new byte[32],
            SpkPublic = new byte[32],
            SpkSignature = new byte[64],
            SpkCreatedAt = DateTimeOffset.UtcNow.AddSeconds(-60)
        };
        var newer = new UserKeyBundle
        {
            UserId = userId,
            DeviceId = newerDeviceId,
            IkPublic = new byte[32],
            SpkPublic = new byte[32],
            SpkSignature = new byte[64],
            SpkCreatedAt = DateTimeOffset.UtcNow
        };
        db.UserKeyBundles.Add(older);
        db.UserKeyBundles.Add(newer);
        await db.SaveChangesAsync();

        var result = await sut.GetBundleAsync(userId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.DeviceId.Should().Be(newerDeviceId);
    }

    [Fact]
    public async Task GetBundle_EmptyOpkPool_ReturnsNullOpkFields()
    {
        var (sut, db, _, _) = CreateDeps();
        var userId = Guid.NewGuid();
        var bundle = new UserKeyBundle
        {
            UserId = userId,
            DeviceId = Guid.NewGuid(),
            IkPublic = new byte[32],
            SpkPublic = new byte[32],
            SpkSignature = new byte[64],
            SpkCreatedAt = DateTimeOffset.UtcNow
        };
        db.UserKeyBundles.Add(bundle);
        await db.SaveChangesAsync();

        var result = await sut.GetBundleAsync(userId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.OpkPublic.Should().BeNull();
        result.OpkId.Should().BeNull();
    }
}
