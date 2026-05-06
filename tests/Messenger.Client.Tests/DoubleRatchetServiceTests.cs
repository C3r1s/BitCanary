// Автотест BitCanary: проверка «DoubleRatchetServiceTests».
using Messenger.Client.Avalonia.Services.Crypto;
using Xunit;

namespace Messenger.Client.Tests;

public class DoubleRatchetServiceTests
{
    private static IDoubleRatchetService CreateService() => new DoubleRatchetService();

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }


    [Fact]
    public void InitializeInitiator_CreatesSendingChainKey()
    {
        var svc = CreateService();
        var sharedSecret = RandomBytes(32);
        var spkState = svc.InitializeInitiator(sharedSecret, RandomBytes(32));
        var state = svc.InitializeInitiator(sharedSecret, RandomBytes(32));

        Assert.NotNull(state.SendingChainKey);
        Assert.NotNull(state.RootKey);
        Assert.NotNull(state.DhSendingPublic);
        Assert.NotNull(state.DhSendingPrivate);
        Assert.Equal(0, state.SendMessageNumber);
        Assert.Equal(0, state.ReceiveMessageNumber);
    }

    [Fact]
    public void InitializeResponder_SetsRootKeyAndDhKeys()
    {
        var svc = CreateService();
        var sharedSecret = RandomBytes(32);
        var spkPrivate = RandomBytes(32);
        var spkPublic = RandomBytes(32);

        var state = svc.InitializeResponder(sharedSecret, spkPrivate, spkPublic);

        Assert.NotNull(state.RootKey);
        Assert.NotNull(state.DhSendingPrivate);
        Assert.NotNull(state.DhSendingPublic);
        Assert.Null(state.ReceivingChainKey); // derived on first Decrypt
        Assert.Equal(0, state.SendMessageNumber);
    }


    [Fact]
    public void TwoPartyExchange_FiveMessages_ProducesUniqueCiphertext()
    {
        var (alice, bob) = CreateAliceBobSession();
        var svc = CreateService();
        var ad = System.Text.Encoding.UTF8.GetBytes("assoc-data");

        var ciphertexts = new List<byte[]>();
        var plaintexts = new[] { "msg0", "msg1", "msg2", "msg3", "msg4" };
        for (int i = 0; i < 5; i++)
        {
            if (i % 2 == 0)
            {
                var (ct, rpub, pn, n) = svc.Encrypt(alice, System.Text.Encoding.UTF8.GetBytes(plaintexts[i]), ad);
                ciphertexts.Add(ct);
                var pt = svc.Decrypt(bob, ct, rpub, pn, n, ad,
                    (_, _) => null,
                    (rpk, mn, mk) => { });
                Assert.Equal(plaintexts[i], System.Text.Encoding.UTF8.GetString(pt));
            }
            else
            {
                var (ct, rpub, pn, n) = svc.Encrypt(bob, System.Text.Encoding.UTF8.GetBytes(plaintexts[i]), ad);
                ciphertexts.Add(ct);
                var pt = svc.Decrypt(alice, ct, rpub, pn, n, ad,
                    (_, _) => null,
                    (rpk, mn, mk) => { });
                Assert.Equal(plaintexts[i], System.Text.Encoding.UTF8.GetString(pt));
            }
        }

        for (int i = 0; i < ciphertexts.Count; i++)
            for (int j = i + 1; j < ciphertexts.Count; j++)
                Assert.False(ciphertexts[i].SequenceEqual(ciphertexts[j]),
                    $"Ciphertext {i} and {j} should not be equal");
    }

    [Fact]
    public void OutOfOrderDecryption_SucceedsViaSkippedKeys()
    {
        var (alice, bob) = CreateAliceBobSession();
        var svc = CreateService();
        var ad = System.Text.Encoding.UTF8.GetBytes("test");

        var skippedKeyStore = new Dictionary<(byte[], int), byte[]>(
            new SkippedKeyComparer());

        var msgs = new List<(byte[] ct, byte[] rpub, int pn, int n)>();
        for (int i = 0; i < 3; i++)
        {
            var pt = System.Text.Encoding.UTF8.GetBytes($"message-{i}");
            var (ct, rpub, pn, n) = svc.Encrypt(alice, pt, ad);
            msgs.Add((ct, rpub, pn, n));
        }

        for (int i = 2; i >= 0; i--)
        {
            var (ct, rpub, pn, n) = msgs[i];
            var pt = svc.Decrypt(bob, ct, rpub, pn, n, ad,
                (rpk, mn) =>
                {
                    foreach (var kvp in skippedKeyStore)
                        if (kvp.Key.Item1.SequenceEqual(rpk) && kvp.Key.Item2 == mn)
                            return kvp.Value;
                    return null;
                },
                (rpk, mn, mk) => skippedKeyStore[(rpk, mn)] = mk);

            Assert.Equal($"message-{i}", System.Text.Encoding.UTF8.GetString(pt));
        }
    }


    private static (RatchetState alice, RatchetState bob) CreateAliceBobSession()
    {
        var svc = CreateService();
        var sharedSecret = RandomBytes(32);

        var bobTempPrivate = RandomBytes(32);
        var bobTempPublic = RandomBytes(32);
        var bob = svc.InitializeResponder(sharedSecret, bobTempPrivate, bobTempPublic);
        bob.SessionId = "test-session";

        var alice = CreateAliceWithRealDh(svc, sharedSecret, out var realBobSpkPrivate, out var realBobSpkPublic);
        bob = svc.InitializeResponder(sharedSecret, realBobSpkPrivate, realBobSpkPublic);
        bob.SessionId = "test-session";
        alice.SessionId = "test-session";

        return (alice, bob);
    }

    private static RatchetState CreateAliceWithRealDh(
        IDoubleRatchetService svc,
        byte[] sharedSecret,
        out byte[] bobSpkPrivate,
        out byte[] bobSpkPublic)
    {
        var bobSpkKey = NSec.Cryptography.Key.Create(
            NSec.Cryptography.KeyAgreementAlgorithm.X25519,
            new NSec.Cryptography.KeyCreationParameters
            {
                ExportPolicy = NSec.Cryptography.KeyExportPolicies.AllowPlaintextExport
            });
        bobSpkPrivate = bobSpkKey.Export(NSec.Cryptography.KeyBlobFormat.RawPrivateKey);
        bobSpkPublic = bobSpkKey.PublicKey.Export(NSec.Cryptography.KeyBlobFormat.RawPublicKey);

        return svc.InitializeInitiator(sharedSecret, bobSpkPublic);
    }

    private sealed class SkippedKeyComparer : IEqualityComparer<(byte[], int)>
    {
        public bool Equals((byte[], int) x, (byte[], int) y) =>
            x.Item2 == y.Item2 && x.Item1.SequenceEqual(y.Item1);

        public int GetHashCode((byte[], int) obj) =>
            HashCode.Combine(obj.Item1.Length, obj.Item2);
    }
}
