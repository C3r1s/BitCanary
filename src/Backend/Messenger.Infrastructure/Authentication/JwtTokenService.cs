// Подпись и выпуск access JWT для пользователей BitCanary.
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Messenger.Application.Abstractions;
using Messenger.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Messenger.Infrastructure.Authentication;

public sealed class JwtTokenService(IOptions<JwtOptions> jwtOptions) : ITokenService
{
    private readonly JwtOptions _options = jwtOptions.Value;

    public string CreateAccessToken(User user)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
