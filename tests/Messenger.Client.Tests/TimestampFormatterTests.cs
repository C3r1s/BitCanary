// Автотест BitCanary: проверка «TimestampFormatterTests».
using Messenger.Client.Avalonia.ViewModels;
using Xunit;

namespace Messenger.Client.Tests;

[Trait("Category", "Unit")]
public sealed class TimestampFormatterTests
{
    private static readonly TimeSpan LocalOffset = TimeZoneInfo.Local.BaseUtcOffset;

    private static readonly DateTimeOffset TestNow =
        new DateTimeOffset(2026, 4, 5, 14, 0, 0, LocalOffset); // Sunday 2026-04-05 14:00 local


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


    [Fact]
    public void Format_SameDayToday_ReturnsHHmm()
    {
        var ts = new DateTimeOffset(2026, 4, 5, 8, 30, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("08:30", result);
    }

    [Fact]
    public void Format_TodayAt1432_Returns1432()
    {
        var now = new DateTimeOffset(2026, 4, 5, 16, 33, 0, LocalOffset);
        var ts  = new DateTimeOffset(2026, 4, 5, 14, 32, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, now);
        Assert.Equal("14:32", result);
    }


    [Fact]
    public void Format_3DaysAgo_ReturnsDayAbbreviation()
    {
        var ts = new DateTimeOffset(2026, 4, 2, 10, 0, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("Thu", result);
    }

    [Fact]
    public void Format_6DaysAgo_ReturnsDayAbbreviation()
    {
        var ts = new DateTimeOffset(2026, 3, 30, 10, 0, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("Mon", result);
    }


    [Fact]
    public void Format_10DaysAgo_ReturnsMMMd()
    {
        var ts = new DateTimeOffset(2026, 3, 26, 10, 0, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("Mar 26", result);
    }

    [Fact]
    public void Format_30DaysAgo_ReturnsMMMd()
    {
        var ts = new DateTimeOffset(2026, 3, 6, 10, 0, 0, LocalOffset);
        var result = TimestampFormatter.FormatTimestamp(ts, TestNow);
        Assert.Equal("Mar 6", result);
    }
}
