using Messenger.Application.Abstractions;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public Task<AuthResponse> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        authService.RegisterAsync(request, cancellationToken);

    [HttpPost("login")]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken) =>
        authService.LoginAsync(request, cancellationToken);
}
