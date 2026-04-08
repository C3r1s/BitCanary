using Xunit;

namespace Messenger.Client.Tests.Notifications;

/// <summary>
/// Unit tests for toast activation argument parsing.
/// Wave 0 stubs — implementations added after App.axaml.cs OnActivated handler exists.
/// </summary>
public sealed class NotificationActivationTests
{
    [Fact(Skip = "Wave 0 stub — implement after App.axaml.cs OnActivated handler exists")]
    public void ToastArguments_WithValidChatId_ParsesGuid()
    {
        // Arrange: argument string "chatId=3fa85f64-5717-4562-b3fc-2c963f66afa6"
        // Act: ToastArguments.Parse(argString).TryGetValue("chatId", out var val); Guid.TryParse(val, out var id)
        // Assert: id == new Guid("3fa85f64-5717-4562-b3fc-2c963f66afa6")
        Assert.True(true);
    }

    [Fact(Skip = "Wave 0 stub — implement after App.axaml.cs OnActivated handler exists")]
    public void ToastArguments_WithMissingChatId_DoesNotNavigate()
    {
        // Arrange: argument string "" (empty)
        // Act: parse and attempt navigation
        // Assert: NavigateToChatAsync never called
        Assert.True(true);
    }
}
