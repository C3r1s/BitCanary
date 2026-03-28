namespace Messenger.Application.Tests.Keys;

public sealed class KeyBundleServiceTests
{
    [Fact(Skip = "Stub - Plan 03 implements")]
    public void UploadBundle_NewDevice_AssignsDeviceIdAndReturns201()
    {
        Assert.True(true);
    }

    [Fact(Skip = "Stub - Plan 03 implements")]
    public void UploadBundle_InvalidSpkSignature_ThrowsAppException()
    {
        Assert.True(true);
    }

    [Fact(Skip = "Stub - Plan 03 implements")]
    public void UploadBundle_ExistingDevice_UpdatesSpkInPlace()
    {
        Assert.True(true);
    }

    [Fact(Skip = "Stub - Plan 03 implements")]
    public void GetBundle_WithOpks_ReturnsOneOpkAndMarksItClaimed()
    {
        Assert.True(true);
    }

    [Fact(Skip = "Stub - Plan 03 implements")]
    public void GetBundle_NoOpks_ReturnsPartialBundleWithNullOpk()
    {
        Assert.True(true);
    }

    [Fact(Skip = "Requires PostgreSQL")]
    public void GetBundle_ConcurrentCalls_NeverReturnSameOpk()
    {
        Assert.True(true);
    }

    [Fact(Skip = "Stub - Plan 03 implements")]
    public void ReplenishOpks_AddsKeysToPool()
    {
        Assert.True(true);
    }

    [Fact(Skip = "Stub - Plan 03 implements")]
    public void ReplenishOpks_ExceedsMaxBatchSize_ThrowsAppException()
    {
        Assert.True(true);
    }

    [Fact(Skip = "Stub - Plan 03 implements")]
    public void OtpkSupplyLow_FiresOnTransitionFromGte10ToLt10()
    {
        Assert.True(true);
    }

    [Fact(Skip = "Stub - Plan 03 implements")]
    public void OtpkSupplyLow_DoesNotRefireWhenAlreadyBelow10()
    {
        Assert.True(true);
    }

    [Fact(Skip = "Stub - Plan 03 implements")]
    public void UploadBundle_SpkRotation_PreservesExistingOpks()
    {
        Assert.True(true);
    }
}
