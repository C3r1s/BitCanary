// Автотест BitCanary: проверка «IdentityKeyChangeTests».
using Messenger.Client.Avalonia.Services.Crypto;
using Xunit;

namespace Messenger.Client.Tests.Security;

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
        IIdentityKeyChangeDetector detector = new IdentityKeyChangeDetector();
        byte[]? storedIk = null;
        var incomingIk = MakeKey(0xAA);

        var changed = detector.HasKeyChanged(storedIk, incomingIk);

        Assert.False(changed);
    }

    [Fact]
    public void MatchingIk_DoesNotTriggerAlert()
    {
        IIdentityKeyChangeDetector detector = new IdentityKeyChangeDetector();
        var storedIk   = MakeKey(0x55);
        var incomingIk = MakeKey(0x55);  // same bytes

        var changed = detector.HasKeyChanged(storedIk, incomingIk);

        Assert.False(changed);
    }

    [Fact]
    public void DifferentIk_TriggersAlert()
    {
        IIdentityKeyChangeDetector detector = new IdentityKeyChangeDetector();
        var storedIk   = MakeKey(0x11);
        var incomingIk = MakeKey(0xFF);  // different bytes

        var changed = detector.HasKeyChanged(storedIk, incomingIk);

        Assert.True(changed);
    }
}
