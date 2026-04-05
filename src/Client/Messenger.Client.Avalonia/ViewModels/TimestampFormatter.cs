namespace Messenger.Client.Avalonia.ViewModels;

/// <summary>
/// Formats a <see cref="DateTimeOffset"/> timestamp into a human-readable relative string
/// for use in chat list items (per D-07).
/// </summary>
/// <remarks>
/// Rules (per UI-SPEC Chat List Item Enrichment):
/// <list type="bullet">
///   <item>Less than 60 minutes ago → "{N}m ago"</item>
///   <item>Same calendar day (but &gt;= 60 min ago) → "HH:mm"</item>
///   <item>Less than 7 calendar days ago → abbreviated day name (e.g. "Mon")</item>
///   <item>7 or more calendar days ago → "MMM d" (e.g. "Mar 26")</item>
/// </list>
/// All comparisons use local time so the rules match what the user sees on their clock.
/// </remarks>
public static class TimestampFormatter
{
    /// <summary>
    /// Formats <paramref name="timestamp"/> relative to <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    public static string FormatTimestamp(DateTimeOffset timestamp)
        => FormatTimestamp(timestamp, DateTimeOffset.UtcNow);

    /// <summary>
    /// Formats <paramref name="timestamp"/> relative to <paramref name="now"/>.
    /// Overload exists to allow deterministic unit tests without mocking the clock.
    /// </summary>
    public static string FormatTimestamp(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var localTs  = timestamp.ToLocalTime();
        var localNow = now.ToLocalTime();

        var elapsed = localNow - localTs;

        // < 60 minutes → "Xm ago"
        if (elapsed.TotalMinutes < 60)
        {
            var minutes = (int)Math.Max(0, Math.Floor(elapsed.TotalMinutes));
            return $"{minutes}m ago";
        }

        // Same calendar day → "HH:mm"
        if (localTs.Date == localNow.Date)
        {
            return localTs.ToString("HH:mm");
        }

        // < 7 days → abbreviated day name
        if (elapsed.TotalDays < 7)
        {
            return localTs.ToString("ddd");
        }

        // Older → "MMM d"
        return localTs.ToString("MMM d");
    }
}
