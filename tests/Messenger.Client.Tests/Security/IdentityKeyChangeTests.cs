using Messenger.Client.Avalonia.Services.Crypto;
using Xunit;

namespace Messenger.Client.Tests.Security;

/// <summary>
/// Unit tests for IIdentityKeyChangeDetector — Wave 0 stubs (RED until 04-03 creates the interface).
/// Covers KEY-02: null IK no-op, matching IK silent, differing IK triggers alert.
/// </summary>
public class IdentityKeyChangeTests
{
    private static byte[] MakeKey(byte seed)
    {
        var key = new byte[32];
        for (int i = 0; i < 32; i++) key[i] = (byte)(seed + i);
        return key;
    }

    [Fact]
    public void NullStoredIk_DoesNotTriggerAlert()
    {
        // Arrange — no previously stored IK (first message from this contact)
        IIdentityKeyChangeDetector detector = new IdentityKeyChangeDetector();
        byte[]? storedIk = null;
        var incomingIk = MakeKey(0xAA);

        // Act
        var changed = detector.HasKeyChanged(storedIk, incomingIk);

        // Assert — null stored IK must NEVER trigger a false-positive alert (Pitfall 3)
        Assert.False(changed);
    }

    [Fact]
    public void MatchingIk_DoesNotTriggerAlert()
    {
        // Arrange — same key seen again (normal case)
        IIdentityKeyChangeDetector detector = new IdentityKeyChangeDetector();
        var storedIk   = MakeKey(0x55);
        var incomingIk = MakeKey(0x55);  // same bytes

        // Act
        var changed = detector.HasKeyChanged(storedIk, incomingIk);

        // Assert
        Assert.False(changed);
    }

    [Fact]
    public void DifferentIk_TriggersAlert()
    {
        // Arrange — contact has reinstalled / changed device (MITM scenario)
        IIdentityKeyChangeDetector detector = new IdentityKeyChangeDetector();
        var storedIk   = MakeKey(0x11);
        var incomingIk = MakeKey(0xFF);  // different bytes

        // Act
        var changed = detector.HasKeyChanged(storedIk, incomingIk);

        // Assert
        Assert.True(changed);
    }
}
