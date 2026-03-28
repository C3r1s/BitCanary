using System.Security.Cryptography;
using Messenger.Client.Avalonia.Services.Crypto;
using NSec.Cryptography;
using Xunit;

namespace Messenger.Client.Tests;

public class X3DHServiceTests
{
    private readonly IX3DHService _service = new X3DHService();

    [Fact]
    public void GenerateKeyBundle_ReturnsEd25519IkAndX25519SpkWithValidSignature()
    {
        var bundle = _service.GenerateKeyBundle();

        // IK public is Ed25519 = 32 bytes
        Assert.Equal(32, bundle.IkPublic.Length);
        // SPK public is X25519 = 32 bytes
        Assert.Equal(32, bundle.SpkPublic.Length);
        // SPK signature is Ed25519 = 64 bytes
        Assert.Equal(64, bundle.SpkSignature.Length);

        // Verify SPK signature using Ed25519 IK public
        var ikPub = PublicKey.Import(SignatureAlgorithm.Ed25519, bundle.IkPublic, KeyBlobFormat.RawPublicKey);
        Assert.True(SignatureAlgorithm.Ed25519.Verify(ikPub, bundle.SpkPublic, bundle.SpkSignature));
    }

    [Fact]
    public void GenerateOneTimePreKeys_ReturnsRequestedCountWithX25519Keys()
    {
        var opks = _service.GenerateOneTimePreKeys(5);

        Assert.Equal(5, opks.Count);
        foreach (var opk in opks)
        {
            Assert.Equal(32, opk.Public.Length);
            Assert.NotEmpty(opk.Private);
        }
    }

    [Fact]
    public void TwoPartyHandshake_WithOpk_ProducesIdenticalSharedSecret()
    {
        // Alice (initiator) bundle
        var aliceBundle = _service.GenerateKeyBundle();
        // Bob (responder) bundle
        var bobBundle = _service.GenerateKeyBundle();
        // Bob's OPK
        var bobOpks = _service.GenerateOneTimePreKeys(1);
        var bobOpkId = Guid.NewGuid();

        // Alice initiates with Bob's public material
        var (aliceSk, header) = _service.InitiateSession(
            aliceBundle,
            bobBundle.IkPublic,
            bobBundle.SpkPublic,
            bobBundle.SpkSignature,
            bobOpks[0].Public,
            bobOpkId);

        // Bob responds with his private material and Alice's header
        var bobSk = _service.RespondToSession(
            bobBundle,
            bobOpks[0].Private,
            header);

        Assert.Equal(32, aliceSk.Length);
        Assert.Equal(32, bobSk.Length);
        Assert.Equal(aliceSk, bobSk);
    }

    [Fact]
    public void TwoPartyHandshake_WithoutOpk_ProducesIdenticalSharedSecret()
    {
        var aliceBundle = _service.GenerateKeyBundle();
        var bobBundle = _service.GenerateKeyBundle();

        // No OPK
        var (aliceSk, header) = _service.InitiateSession(
            aliceBundle,
            bobBundle.IkPublic,
            bobBundle.SpkPublic,
            bobBundle.SpkSignature,
            null,
            null);

        var bobSk = _service.RespondToSession(
            bobBundle,
            null,
            header);

        Assert.Equal(32, aliceSk.Length);
        Assert.Equal(32, bobSk.Length);
        Assert.Equal(aliceSk, bobSk);
    }

    [Fact]
    public void InitiateSession_InvalidSpkSignature_ThrowsCryptographicException()
    {
        var aliceBundle = _service.GenerateKeyBundle();
        var bobBundle = _service.GenerateKeyBundle();

        // Corrupt the SPK signature
        var badSignature = new byte[64];
        RandomNumberGenerator.Fill(badSignature);

        Assert.Throws<CryptographicException>(() =>
            _service.InitiateSession(
                aliceBundle,
                bobBundle.IkPublic,
                bobBundle.SpkPublic,
                badSignature,
                null,
                null));
    }

    [Fact]
    public void TwoPartyHandshake_MultipleRounds_AllProduceDistinctSecrets()
    {
        var aliceBundle = _service.GenerateKeyBundle();
        var bobBundle = _service.GenerateKeyBundle();

        var secrets = new HashSet<string>();
        for (int i = 0; i < 3; i++)
        {
            var (sk, header) = _service.InitiateSession(
                aliceBundle,
                bobBundle.IkPublic,
                bobBundle.SpkPublic,
                bobBundle.SpkSignature,
                null,
                null);
            secrets.Add(Convert.ToHexString(sk));
        }

        // Each ephemeral key is random → distinct secrets
        Assert.Equal(3, secrets.Count);
    }
}
