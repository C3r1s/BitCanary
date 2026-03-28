using Messenger.Domain.Abstractions;
using Messenger.Shared.Contracts;

namespace Messenger.Domain.Entities;

public sealed class UserSettings : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public ThemePreference ThemePreference { get; set; } = ThemePreference.System;
    public bool SendByEnter { get; set; } = true;
    public bool UseCompactMode { get; set; }
    public bool EnableCustomEmoji { get; set; } = true;
}
