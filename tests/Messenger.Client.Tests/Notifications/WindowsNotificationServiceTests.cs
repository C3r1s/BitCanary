using Xunit;

namespace Messenger.Client.Tests.Notifications;

/// <summary>
/// Unit tests for WindowsNotificationService guard conditions.
/// Wave 0 stubs — implementations added after WindowsNotificationService exists.
/// </summary>
public sealed class WindowsNotificationServiceTests
{
    [Fact(Skip = "Wave 0 stub — implement after WindowsNotificationService exists")]
    public void ShowIfMinimized_WhenShowNotificationsFalse_DoesNotShow()
    {
        // Arrange: isMinimized = () => true, showNotifications = () => false
        // Act: ShowIfMinimized(Guid.NewGuid(), "Alice")
        // Assert: IToastSender.Send never called
        Assert.True(true);
    }

    [Fact(Skip = "Wave 0 stub — implement after WindowsNotificationService exists")]
    public void ShowIfMinimized_WhenNotMinimized_DoesNotShow()
    {
        // Arrange: isMinimized = () => false, showNotifications = () => true
        // Act: ShowIfMinimized(Guid.NewGuid(), "Alice")
        // Assert: IToastSender.Send never called
        Assert.True(true);
    }

    [Fact(Skip = "Wave 0 stub — implement after WindowsNotificationService exists")]
    public void ShowIfMinimized_WhenShowSenderNameFalse_UsesTitleMessenger()
    {
        // Arrange: isMinimized = () => true, showNotifications = () => true, showSenderName = () => false
        // Act: ShowIfMinimized(someGuid, "Alice")
        // Assert: IToastSender.Send called with title "Messenger"
        Assert.True(true);
    }

    [Fact(Skip = "Wave 0 stub — implement after WindowsNotificationService exists")]
    public void ShowIfMinimized_WhenShowSenderNameTrue_UsesSenderNameAsTitle()
    {
        // Arrange: isMinimized = () => true, showNotifications = () => true, showSenderName = () => true
        // Act: ShowIfMinimized(someGuid, "Alice")
        // Assert: IToastSender.Send called with title "Alice"
        Assert.True(true);
    }

    [Fact(Skip = "Wave 0 stub — implement after WindowsNotificationService exists")]
    public void ShowIfMinimized_ToastArguments_ContainChatIdOnly()
    {
        // Arrange: minimized + notifications on + any sender name setting
        // Act: ShowIfMinimized(specificGuid, "Alice")
        // Assert: captured ToastContent Arguments contains "chatId={specificGuid:D}"
        //         and does NOT contain "Alice" or any message text
        Assert.True(true);
    }
}
