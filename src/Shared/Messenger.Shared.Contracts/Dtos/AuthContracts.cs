namespace Messenger.Shared.Contracts.Dtos;

public sealed record RegisterRequest(string UserName, string DisplayName, string Password, string PublicKey);

public sealed record LoginRequest(string UserName, string Password);

public sealed record AuthResponse(Guid UserId, string UserName, string DisplayName, string AccessToken);
