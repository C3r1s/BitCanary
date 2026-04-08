using Xunit;

namespace Messenger.Client.Tests.Notifications;

/// <summary>
/// Unit tests for notification settings DTO round-trip and ApplySettings behavior.
/// Wave 0 stubs — implementations added after UserSettingsDto and SettingsViewModel are updated.
/// </summary>
public sealed class NotificationSettingsTests
{
    [Fact(Skip = "Wave 0 stub — implement after UserSettingsDto gains ShowNotifications field")]
    public void UserSettingsDto_DefaultValues_ShowNotificationsTrue()
    {
        // Arrange + Act: new UserSettingsDto with only required args, omitting optional
        // Assert: dto.ShowNotifications == true
        Assert.True(true);
    }

    [Fact(Skip = "Wave 0 stub — implement after UserSettingsDto gains ShowSenderName field")]
    public void UserSettingsDto_DefaultValues_ShowSenderNameTrue()
    {
        // Arrange + Act: new UserSettingsDto with only required args, omitting optional
        // Assert: dto.ShowSenderName == true
        Assert.True(true);
    }

    [Fact(Skip = "Wave 0 stub — implement after ApplySettings is updated")]
    public void ApplySettings_WithShowNotificationsFalse_SetsSettingsViewModelFalse()
    {
        // Arrange: UserSettingsDto with ShowNotifications=false
        // Act: MainWindowViewModel.ApplySettings(dto) (or equivalent call)
        // Assert: Settings.ShowNotifications == false
        Assert.True(true);
    }

    [Fact(Skip = "Wave 0 stub — implement after ChangeThemeAsync is updated")]
    public void ChangeThemeAsync_PassesCurrentShowNotificationsValue_NotDefault()
    {
        // Arrange: Settings.ShowNotifications = false; build UpdateSettingsRequest
        // Assert: request.ShowNotifications == false (not the default true)
        Assert.True(true);
    }
}
