using Messenger.Client.Avalonia.Services.Crypto;
using Xunit;

namespace Messenger.Client.Tests.Security;

/// <summary>
/// Unit tests for ISafetyNumberService — Wave 0 stubs (RED until Task 2 implements the service).
/// Covers KEY-01: determinism, symmetry, and formatting of the 60-digit safety number.
/// </summary>
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
        // Arrange
        ISafetyNumberService service = new SafetyNumberService();
        var aliceIk = MakeKey(0x01);
        var bobIk   = MakeKey(0x80);
        var aliceId = "aaaaaaaa-0000-0000-0000-000000000001";
        var bobId   = "bbbbbbbb-0000-0000-0000-000000000002";

        // Act
        var fromAlice = service.Compute(aliceIk, aliceId, bobIk, bobId);
        var fromBob   = service.Compute(bobIk,   bobId,   aliceIk, aliceId);

        // Assert — both parties produce identical safety number
        Assert.Equal(fromAlice, fromBob);
    }

    [Fact]
    public void Compute_Produces60DigitsIn12Groups()
    {
        // Arrange
        ISafetyNumberService service = new SafetyNumberService();
        var localIk  = MakeKey(0x10);
        var remoteIk = MakeKey(0x20);
        var localId  = "cccccccc-0000-0000-0000-000000000003";
        var remoteId = "dddddddd-0000-0000-0000-000000000004";

        // Act
        var result = service.Compute(localIk, localId, remoteIk, remoteId);

        // Assert — exactly 71 chars: 12 groups of 5 decimal digits separated by single spaces
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
        // Arrange
        ISafetyNumberService service = new SafetyNumberService();
        var ik1 = MakeKey(0x01);
        var ik2 = MakeKey(0xFF);
        var uid1 = "11111111-0000-0000-0000-000000000001";
        var uid2 = "22222222-0000-0000-0000-000000000002";
        var uid3 = "33333333-0000-0000-0000-000000000003";
        var uid4 = "44444444-0000-0000-0000-000000000004";

        // Act
        var result1 = service.Compute(ik1, uid1, ik2, uid2);
        var result2 = service.Compute(ik1, uid3, ik2, uid4);

        // Assert — different user IDs produce different fingerprints
        Assert.NotEqual(result1, result2);
    }
}
