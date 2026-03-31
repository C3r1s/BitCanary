using System.Security.Cryptography;
using System.Text;

namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// SHA-512-based safety number computation.
/// Uses 5-byte chunks from the 64-byte digest to produce 12 groups of 5 decimal digits.
/// </summary>
public sealed class SafetyNumberService : ISafetyNumberService
{
    /// <inheritdoc/>
    public string Compute(
        byte[] localIkPublic, string localUserId,
        byte[] remoteIkPublic, string remoteUserId)
    {
        // Canonical sort: lower IK bytes lexicographically goes first.
        // Both parties apply the same sort rule, guaranteeing identical output regardless of caller order.
        bool localFirst = localIkPublic.AsSpan().SequenceCompareTo(remoteIkPublic.AsSpan()) <= 0;
        var (ikA, uidA, ikB, uidB) = localFirst
            ? (localIkPublic, localUserId, remoteIkPublic, remoteUserId)
            : (remoteIkPublic, remoteUserId, localIkPublic, localUserId);

        // Concatenate: IK_A || UTF8(userId_A) || IK_B || UTF8(userId_B)
        var uidABytes = Encoding.UTF8.GetBytes(uidA);
        var uidBBytes = Encoding.UTF8.GetBytes(uidB);
        var input = new byte[ikA.Length + uidABytes.Length + ikB.Length + uidBBytes.Length];
        int pos = 0;
        ikA.CopyTo(input, pos);      pos += ikA.Length;
        uidABytes.CopyTo(input, pos); pos += uidABytes.Length;
        ikB.CopyTo(input, pos);      pos += ikB.Length;
        uidBBytes.CopyTo(input, pos);

        // SHA-512 → 64 bytes; use 12 × 5 = 60 bytes (last 4 bytes unused)
        var hash = SHA512.HashData(input);

        // Format as 12 groups of 5 decimal digits
        // Each group: read 5 bytes as big-endian uint64 (zero-extended), take mod 100000
        var sb = new StringBuilder(71);  // 12*5 digits + 11 spaces = 71 chars
        for (int i = 0; i < 12; i++)
        {
            ulong chunk = 0;
            for (int j = 0; j < 5; j++)
                chunk = (chunk << 8) | hash[i * 5 + j];
            if (i > 0) sb.Append(' ');
            sb.Append((chunk % 100_000UL).ToString("D5"));
        }
        return sb.ToString();
    }
}
