namespace Messenger.Shared.Contracts.Dtos;

public sealed record UserProfileDto(
    Guid Id,
    string UserName,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    DateTimeOffset? LastSeenUtc,
    string PublicKey);

public sealed record UpdateProfileRequest(
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    string PublicKey);

public sealed record UserSettingsDto(
    ThemePreference ThemePreference,
    bool SendByEnter,
    bool UseCompactMode,
    bool EnableCustomEmoji,
    bool ShowNotifications = true,
    bool ShowSenderName = true);

public sealed record UpdateSettingsRequest(
    ThemePreference ThemePreference,
    bool SendByEnter,
    bool UseCompactMode,
    bool EnableCustomEmoji,
    bool ShowNotifications = true,
    bool ShowSenderName = true);
