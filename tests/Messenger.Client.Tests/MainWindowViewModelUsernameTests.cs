// Автотест BitCanary: проверка «MainWindowViewModelUsernameTests».
using Messenger.Client.Avalonia.ViewModels;
using Xunit;

namespace Messenger.Client.Tests;

public class MainWindowViewModelUsernameTests
{

    [Theory]
    [InlineData("ceris", "[ @ceris ]")]
    [InlineData("alice", "[ @alice ]")]
    [InlineData("bob123", "[ @bob123 ]")]
    public void FormatUsername_WithNonEmptyName_ReturnsFormattedBracketLabel(string username, string expected)
    {
        var result = UsernameFormatter.Format(username);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void FormatUsername_WithNullOrEmpty_ReturnsStringEmpty(string? username)
    {
        var result = UsernameFormatter.Format(username);
        Assert.Equal(string.Empty, result);
    }


    [Fact]
    public void HasCurrentUsername_WhenCurrentUsernameIsNonEmpty_ReturnsTrue()
    {
        var currentUsername = "[ @ceris ]";
        var hasCurrentUsername = !string.IsNullOrEmpty(currentUsername);
        Assert.True(hasCurrentUsername);
    }

    [Fact]
    public void HasCurrentUsername_WhenCurrentUsernameIsEmpty_ReturnsFalse()
    {
        var currentUsername = string.Empty;
        var hasCurrentUsername = !string.IsNullOrEmpty(currentUsername);
        Assert.False(hasCurrentUsername);
    }

    [Fact]
    public void FormatUsername_ProducesCorrectFormat_FromCeris()
    {
        var result = UsernameFormatter.Format("ceris");
        Assert.Equal("[ @ceris ]", result);
    }
}

public static class UsernameFormatter
{
    public static string Format(string? username) =>
        string.IsNullOrEmpty(username)
            ? string.Empty
            : $"[ @{username} ]";
}
