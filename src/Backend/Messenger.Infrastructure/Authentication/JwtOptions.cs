namespace Messenger.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "Messenger";
    public string Audience { get; set; } = "Messenger.Client";
    public string SigningKey { get; set; } = "super-secret-development-key-change-me";
    public int ExpirationMinutes { get; set; } = 60;
}
