using FluentAssertions;
using Messenger.Application.Abstractions;
using Messenger.Application.Keys;
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts.Dtos;
using Messenger.Tests.Fakes;

namespace Messenger.Tests;

public sealed class KeyBundleServiceTests
{
    private static (KeyBundleService sut, IAppDbContext db, FakeSpkValidator spk, IRealtimeNotifier notifier) CreateDeps()
    {
        var db = FakeDbContextFactory.Create();
        var spk = new FakeSpkValidator { Result = true };
        var notifier = FakeRealtimeNotifier.Create();
        return (new KeyBundleService(db, spk, notifier), db, spk, notifier);
    }

    [Fact]
    public async Task UploadBundle_NewDevice_SavesAndReturnsDeviceId()
    {
        // Arrange
        var (sut, db, _, _) = CreateDeps();
        var userId = Guid.NewGuid();
        var (ikPublic, spkPublic, spkSignature) = TestKeys.GenerateSignedSpkBundle();
        var request = new KeyBundleUploadRequest(
            DeviceId: null,
            IkPublic: ikPublic,
            SpkPublic: spkPublic,
            SpkSignature: spkSignature);

        // Act
        var response = await sut.UploadBundleAsync(userId, request, CancellationToken.None);

        // Assert — response carries server-assigned DeviceId
        response.DeviceId.Should().NotBe(Guid.Empty);

        // Assert — row persisted with correct fields
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
        // Arrange
        var (sut, _, _, _) = CreateDeps();
        var userId = Guid.NewGuid();
        var (ikPublic, spkPublic, spkSignature) = TestKeys.GenerateSignedSpkBundle();
        await sut.UploadBundleAsync(
            userId,
            new KeyBundleUploadRequest(null, ikPublic, spkPublic, spkSignature),
            CancellationToken.None);

        // Act
        var result = await sut.GetBundleAsync(userId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(userId);
        result.IkPublic.Should().BeEquivalentTo(ikPublic);
        result.SpkPublic.Should().BeEquivalentTo(spkPublic);
        result.SpkSignature.Should().BeEquivalentTo(spkSignature);
    }

    [Fact]
    public async Task GetBundle_WithUnclaimedOpk_AtomicallyClaims()
    {
        // Arrange — seed bundle + one unclaimed OPK
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
            // ClaimedAt intentionally null
        };
        db.UserKeyBundles.Add(bundle);
        db.OneTimePreKeys.Add(opk);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetBundleAsync(userId, CancellationToken.None);

        // Assert — DTO carries OPK
        result.Should().NotBeNull();
        result!.OpkPublic.Should().NotBeNull();
        result.OpkPublic!.Should().BeEquivalentTo(opkBytes);
        result.OpkId.Should().Be(opk.Id);

        // Assert — row is now claimed (atomic claim via InMemory branch)
        var refreshed = db.OneTimePreKeys.Single(x => x.Id == opk.Id);
        refreshed.ClaimedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetBundle_TwoBundlesExist_ReturnsNewest()
    {
        // FIX-02 regression guard — before fix, two bundles for one user threw InvalidOperationException.
        // Arrange
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

        // Act
        var result = await sut.GetBundleAsync(userId, CancellationToken.None);

        // Assert — OrderByDescending(SpkCreatedAt) must pick the newer row
        result.Should().NotBeNull();
        result!.DeviceId.Should().Be(newerDeviceId);
    }

    [Fact]
    public async Task GetBundle_EmptyOpkPool_ReturnsNullOpkFields()
    {
        // Arrange — bundle exists, zero OPKs
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

        // Act
        var result = await sut.GetBundleAsync(userId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.OpkPublic.Should().BeNull();
        result.OpkId.Should().BeNull();
    }
}
