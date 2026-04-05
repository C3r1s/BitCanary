using Messenger.Client.Avalonia.ViewModels;
using Xunit;

namespace Messenger.Client.Tests;

/// <summary>
/// Tests for TimestampFormatter. All timestamps use the local time zone offset
/// so that ToLocalTime() is idempotent (offset == local offset → no shift).
/// </summary>
[Trait("Category", "Unit")]
public sealed class TimestampFormatterTests
{
    // Use local offset so that timestamp.ToLocalTime() == timestamp (no shift)
    private static readonly TimeSpan LocalOffset = TimeZoneInfo.Local.BaseUtcOffset;

    // "now" base: a Sunday at 14:00 local time — chosen to give clear day-of-week results
    private static readonly DateTimeOffset TestNow =
        new DateTimeOffset(2026, 4, 5, 14, 0, 0, LocalOffset); // Sunday 2026-04-05 14:00 local

    // ── < 60 minutes ago ─────────────────────────────────────────────────

    [Fact]
    public void Format_ZeroMinutesAgo_Returns0mAgo()
    {
        var result = TimestampFormatter.FormatTimestamp(TestNow, TestNow);
        Assert.Equal("0m ago", result);
    }

    [Fact]
    public void Format_5MinutesAgo_Returns5mAgo()
    {
        var ts = TestNow.AddMinutes(-5);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("5m ago", result);
    }

    [Fact]
    public void Format_45MinutesAgo_Returns45mAgo()
    {
        var ts = TestNow.AddMinutes(-45);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("45m ago", result);
    }

    [Fact]
    public void Format_59MinutesAgo_Returns59mAgo()
    {
        var ts = TestNow.AddMinutes(-59);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("59m ago", result);
    }

    // ── Same day (but >= 60 minutes ago) ─────────────────────────────────

    [Fact]
    public void Format_SameDayToday_ReturnsHHmm()
    {
        // TestNow is 14:00 local; timestamp is 08:30 same day local
        var ts = new DateTimeOffset(2026, 4, 5, 8, 30, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("08:30", result);
    }

    [Fact]
    public void Format_TodayAt1432_Returns1432()
    {
        // now is 16:33 local (>= 61 minutes after ts); timestamp is 14:32 same day local
        // elapsed = 121 min → NOT "Xm ago", same calendar day → "HH:mm"
        var now = new DateTimeOffset(2026, 4, 5, 16, 33, 0, LocalOffset);
        var ts  = new DateTimeOffset(2026, 4, 5, 14, 32, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, now);
        Assert.Equal("14:32", result);
    }

    // ── < 7 days ago (but not same day) ──────────────────────────────────

    [Fact]
    public void Format_3DaysAgo_ReturnsDayAbbreviation()
    {
        // TestNow is Sunday 2026-04-05; 3 days ago is Thursday 2026-04-02 local
        var ts = new DateTimeOffset(2026, 4, 2, 10, 0, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        // Thursday abbreviated: "Thu"
        Assert.Equal("Thu", result);
    }

    [Fact]
    public void Format_6DaysAgo_ReturnsDayAbbreviation()
    {
        // 6 days before Sunday = Monday 2026-03-30
        var ts = new DateTimeOffset(2026, 3, 30, 10, 0, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("Mon", result);
    }

    // ── 7+ days ago ───────────────────────────────────────────────────────

    [Fact]
    public void Format_10DaysAgo_ReturnsMMMd()
    {
        // 10 days before 2026-04-05 = 2026-03-26
        var ts = new DateTimeOffset(2026, 3, 26, 10, 0, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("Mar 26", result);
    }

    [Fact]
    public void Format_30DaysAgo_ReturnsMMMd()
    {
        // 30 days before 2026-04-05 = 2026-03-06
        var ts = new DateTimeOffset(2026, 3, 6, 10, 0, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("Mar 6", result);
    }
}
