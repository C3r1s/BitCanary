namespace Messenger.Shared.Contracts.Realtime;

public sealed record PresenceChangedDto(Guid UserId, DateTimeOffset? LastSeenUtc, bool IsOnline);
