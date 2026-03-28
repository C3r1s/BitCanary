using Messenger.Client.Avalonia.Services.Crypto;
using Xunit;

namespace Messenger.Client.Tests;

/// <summary>
/// Unit tests for DoubleRatchetService — verifies DR algorithm correctness.
/// Uses in-memory delegates for skipped key storage (no SQLite dependency).
/// </summary>
public class DoubleRatchetServiceTests
{
    private static IDoubleRatchetService CreateService() => new DoubleRatchetService();

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    // ── Initialization ────────────────────────────────────────────────────

    [Fact]
    public void InitializeInitiator_CreatesSendingChainKey()
    {
        var svc = CreateService();
        var sharedSecret = RandomBytes(32);
        // Generate a temporary X25519 key pair to simulate SPK
        var spkState = svc.InitializeInitiator(sharedSecret, RandomBytes(32));
        // Initiator hack: create a real SPK via InitializeResponder and use its public key
        // For this test, we just verify state shape.
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

    // ── Two-party message exchange ────────────────────────────────────────

    [Fact]
    public void TwoPartyExchange_FiveMessages_ProducesUniqueCiphertext()
    {
        var (alice, bob) = CreateAliceBobSession();
        var svc = CreateService();
        var ad = System.Text.Encoding.UTF8.GetBytes("assoc-data");

        var ciphertexts = new List<byte[]>();
        var plaintexts = new[] { "msg0", "msg1", "msg2", "msg3", "msg4" };
        // Alternate sender: Alice sends even, Bob sends odd
        for (int i = 0; i < 5; i++)
        {
            if (i % 2 == 0)
            {
                // Alice encrypts
                var (ct, rpub, pn, n) = svc.Encrypt(alice, System.Text.Encoding.UTF8.GetBytes(plaintexts[i]), ad);
                ciphertexts.Add(ct);
                // Bob decrypts
                var pt = svc.Decrypt(bob, ct, rpub, pn, n, ad,
                    (_, _) => null,
                    (rpk, mn, mk) => { });
                Assert.Equal(plaintexts[i], System.Text.Encoding.UTF8.GetString(pt));
            }
            else
            {
                // Bob encrypts
                var (ct, rpub, pn, n) = svc.Encrypt(bob, System.Text.Encoding.UTF8.GetBytes(plaintexts[i]), ad);
                ciphertexts.Add(ct);
                // Alice decrypts
                var pt = svc.Decrypt(alice, ct, rpub, pn, n, ad,
                    (_, _) => null,
                    (rpk, mn, mk) => { });
                Assert.Equal(plaintexts[i], System.Text.Encoding.UTF8.GetString(pt));
            }
        }

        // All ciphertexts must be distinct (unique per-message key)
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

        // Alice sends 3 messages
        var skippedKeyStore = new Dictionary<(byte[], int), byte[]>(
            new SkippedKeyComparer());

        var msgs = new List<(byte[] ct, byte[] rpub, int pn, int n)>();
        for (int i = 0; i < 3; i++)
        {
            var pt = System.Text.Encoding.UTF8.GetBytes($"message-{i}");
            var (ct, rpub, pn, n) = svc.Encrypt(alice, pt, ad);
            msgs.Add((ct, rpub, pn, n));
        }

        // Bob decrypts in reverse order (2, 1, 0)
        for (int i = 2; i >= 0; i--)
        {
            var (ct, rpub, pn, n) = msgs[i];
            var pt = svc.Decrypt(bob, ct, rpub, pn, n, ad,
                (rpk, mn) =>
                {
                    // Try to find a stored skipped key
                    foreach (var kvp in skippedKeyStore)
                        if (kvp.Key.Item1.SequenceEqual(rpk) && kvp.Key.Item2 == mn)
                            return kvp.Value;
                    return null;
                },
                (rpk, mn, mk) => skippedKeyStore[(rpk, mn)] = mk);

            Assert.Equal($"message-{i}", System.Text.Encoding.UTF8.GetString(pt));
        }
    }

    // ── Helper ───────────────────────────────────────────────────────────

    private static (RatchetState alice, RatchetState bob) CreateAliceBobSession()
    {
        var svc = CreateService();
        // Generate a shared secret (simulating X3DH output)
        var sharedSecret = RandomBytes(32);

        // Bob's SPK: we need a real X25519 keypair. Use DoubleRatchetService's
        // InitializeResponder to get bob, and extract his SPK public key from state.
        // For test purposes, generate raw bytes as the "SPK" key pair.
        // The real X25519 key generation happens inside InitializeInitiator,
        // so we create bob first and read his DhSendingPublic.
        var bobTempPrivate = RandomBytes(32);
        var bobTempPublic = RandomBytes(32);
        var bob = svc.InitializeResponder(sharedSecret, bobTempPrivate, bobTempPublic);
        bob.SessionId = "test-session";

        // Alice is the initiator; she needs bob's SPK public key.
        // Since InitializeInitiator performs DH, we need a REAL X25519 key pair for bob.
        // Let's use NSec to generate a proper key pair, then wire up alice and bob correctly.
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
        // Use NSec X25519 to generate a real Bob SPK key pair
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

    // Equality comparer for (byte[], int) keys in Dictionary
    private sealed class SkippedKeyComparer : IEqualityComparer<(byte[], int)>
    {
        public bool Equals((byte[], int) x, (byte[], int) y) =>
            x.Item2 == y.Item2 && x.Item1.SequenceEqual(y.Item1);

        public int GetHashCode((byte[], int) obj) =>
            HashCode.Combine(obj.Item1.Length, obj.Item2);
    }
}
