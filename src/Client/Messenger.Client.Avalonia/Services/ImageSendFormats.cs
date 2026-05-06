// Сервис клиента BitCanary: сеть, кэш, медиа — «ImageSendFormats».
using System.IO;
using System.Text.RegularExpressions;

namespace Messenger.Client.Avalonia.Services;

internal static class ImageSendFormats
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
    };

    private static readonly Regex LegacyImageFileLine = new(
        @"^\[File:\s*(.+\.(?:png|jpg|jpeg|gif|webp|bmp))\s*\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsImageFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var ext = Path.GetExtension(fileName);
        return Extensions.Contains(ext);
    }

    public static string GuessContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }

    public static bool IsLegacyImageFileLine(string? decryptedText)
    {
        if (string.IsNullOrWhiteSpace(decryptedText)) return false;
        var t = decryptedText.Trim();
        return LegacyImageFileLine.IsMatch(t);
    }
}
