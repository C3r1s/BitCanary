// Автотест BitCanary: проверка «SafetyNumberServiceTests».
using Messenger.Client.Avalonia.Services.Crypto;
using Xunit;

namespace Messenger.Client.Tests.Security;

public class SafetyNumberServiceTests
{
    private static byte[] MakeKey(byte seed)
    {
        var key = new byte[32];
        for (int i = 0; i < 32; i++) key[i] = (byte)(seed + i);
        return key;
    }

    [Fact]
    public void Compute_IsSymmetric()
    {
        ISafetyNumberService service = new SafetyNumberService();
        var aliceIk = MakeKey(0x01);
        var bobIk   = MakeKey(0x80);
        var aliceId = "aaaaaaaa-0000-0000-0000-000000000001";
        var bobId   = "bbbbbbbb-0000-0000-0000-000000000002";

        var fromAlice = service.Compute(aliceIk, aliceId, bobIk, bobId);
        var fromBob   = service.Compute(bobIk,   bobId,   aliceIk, aliceId);

        Assert.Equal(fromAlice, fromBob);
    }

    [Fact]
    public void Compute_Produces60DigitsIn12Groups()
    {
        ISafetyNumberService service = new SafetyNumberService();
        var localIk  = MakeKey(0x10);
        var remoteIk = MakeKey(0x20);
        var localId  = "cccccccc-0000-0000-0000-000000000003";
        var remoteId = "dddddddd-0000-0000-0000-000000000004";

        var result = service.Compute(localIk, localId, remoteIk, remoteId);

        Assert.Equal(71, result.Length);
        var groups = result.Split(' ');
        Assert.Equal(12, groups.Length);
        foreach (var group in groups)
        {
            Assert.Equal(5, group.Length);
            Assert.True(group.All(char.IsDigit), $"Non-digit chars in group: '{group}'");
        }
    }

    [Fact]
    public void Compute_DifferentInputs_ProduceDifferentNumbers()
    {
        ISafetyNumberService service = new SafetyNumberService();
        var ik1 = MakeKey(0x01);
        var ik2 = MakeKey(0xFF);
        var uid1 = "11111111-0000-0000-0000-000000000001";
        var uid2 = "22222222-0000-0000-0000-000000000002";
        var uid3 = "33333333-0000-0000-0000-000000000003";
        var uid4 = "44444444-0000-0000-0000-000000000004";

        var result1 = service.Compute(ik1, uid1, ik2, uid2);
        var result2 = service.Compute(ik1, uid3, ik2, uid4);

        Assert.NotEqual(result1, result2);
    }
}
