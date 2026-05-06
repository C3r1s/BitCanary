// Состояние и команды UI BitCanary для «TimestampFormatter».
namespace Messenger.Client.Avalonia.ViewModels;

public static class TimestampFormatter
{
    public static string FormatTimestamp(DateTimeOffset timestamp)
        => FormatTimestamp(timestamp, DateTimeOffset.UtcNow);

    public static string FormatTimestamp(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var localTs  = timestamp.ToLocalTime();
        var localNow = now.ToLocalTime();

        var elapsed = localNow - localTs;

        if (elapsed.TotalMinutes < 60)
        {
            var minutes = (int)Math.Max(0, Math.Floor(elapsed.TotalMinutes));
            return $"{minutes}m ago";
        }

        if (localTs.Date == localNow.Date)
        {
            return localTs.ToString("HH:mm");
        }

        if (elapsed.TotalDays < 7)
        {
            return localTs.ToString("ddd");
        }

        return localTs.ToString("MMM d");
    }
}
