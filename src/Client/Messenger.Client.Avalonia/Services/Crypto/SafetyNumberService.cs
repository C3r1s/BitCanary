// Клиентское E2E: «SafetyNumberService» (сессии, ключи, ratchet).
using System.Security.Cryptography;
using System.Text;

namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed class SafetyNumberService : ISafetyNumberService
{
    public string Compute(
        byte[] localIkPublic, string localUserId,
        byte[] remoteIkPublic, string remoteUserId)
    {
        bool localFirst = localIkPublic.AsSpan().SequenceCompareTo(remoteIkPublic.AsSpan()) <= 0;
        var (ikA, uidA, ikB, uidB) = localFirst
            ? (localIkPublic, localUserId, remoteIkPublic, remoteUserId)
            : (remoteIkPublic, remoteUserId, localIkPublic, localUserId);

        var uidABytes = Encoding.UTF8.GetBytes(uidA);
        var uidBBytes = Encoding.UTF8.GetBytes(uidB);
        var input = new byte[ikA.Length + uidABytes.Length + ikB.Length + uidBBytes.Length];
        int pos = 0;
        ikA.CopyTo(input, pos);      pos += ikA.Length;
        uidABytes.CopyTo(input, pos); pos += uidABytes.Length;
        ikB.CopyTo(input, pos);      pos += ikB.Length;
        uidBBytes.CopyTo(input, pos);

        var hash = SHA512.HashData(input);

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
