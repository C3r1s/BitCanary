using System.Net.Http.Headers;
using System.Net.Http.Json;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// HTTP client wrapper for the Messenger REST API.
/// Uses a per-request Authorization header so that the token is always
/// up-to-date even after a mid-session login (token is read from
/// IClientSessionService on every call).
/// </summary>
public sealed class MessengerApiClient(IClientSessionService sessionService) : IMessengerApiClient
{
    // One HttpClient for the lifetime of the app — BaseAddress is stable.
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri($"{sessionService.ApiBaseUrl.TrimEnd('/')}/")
    };

    // ── Auth ────────────────────────────────────────────────────────────

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken))!;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/auth/register", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken))!;
    }

    // ── Chats & messages ────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<ChatSummaryDto>> GetChatsAsync(CancellationToken cancellationToken = default)
    {
        if (!sessionService.IsAuthenticated) return Array.Empty<ChatSummaryDto>();
        using var req = Authorized(HttpMethod.Get, "api/chats");
        var response = await _http.SendAsync(req, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<ChatSummaryDto>>(cancellationToken)
               ?? Array.Empty<ChatSummaryDto>();
    }

    public async Task<IReadOnlyCollection<MessageDto>> GetMessagesAsync(Guid chatId, CancellationToken cancellationToken = default)
    {
        if (!sessionService.IsAuthenticated) return Array.Empty<MessageDto>();
        using var req = Authorized(HttpMethod.Get, $"api/chats/{chatId}/messages");
        var response = await _http.SendAsync(req, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<MessageDto>>(cancellationToken)
               ?? Array.Empty<MessageDto>();
    }

    public async Task<MessageDto> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        using var req = Authorized(HttpMethod.Post, "api/messages");
        req.Content = JsonContent.Create(request);
        var response = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MessageDto>(cancellationToken))!;
    }

    // ── User settings ───────────────────────────────────────────────────

    public async Task<UserSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var fallback = new UserSettingsDto(ThemePreference.System, true, false, true);
        if (!sessionService.IsAuthenticated) return fallback;

        using var req = Authorized(HttpMethod.Get, "api/users/me/settings");
        var response = await _http.SendAsync(req, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserSettingsDto>(cancellationToken) ?? fallback;
    }

    public async Task<UserSettingsDto> UpdateSettingsAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default)
    {
        using var req = Authorized(HttpMethod.Put, "api/users/me/settings");
        req.Content = JsonContent.Create(request);
        var response = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<UserSettingsDto>(cancellationToken))!;
    }

    // ── Media ───────────────────────────────────────────────────────────

    public async Task<MediaUploadResponse> UploadMediaAsync(
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        using var req = Authorized(HttpMethod.Post, "api/media/upload");

        var multipart = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        multipart.Add(fileContent, "file", fileName);
        req.Content = multipart;

        var response = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MediaUploadResponse>(cancellationToken))!;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a request with the current bearer token attached.
    /// Reading the token from sessionService on every call ensures it is
    /// always fresh (e.g. after a mid-session re-login).
    /// </summary>
    private HttpRequestMessage Authorized(HttpMethod method, string uri)
    {
        var msg = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(sessionService.AccessToken))
        {
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionService.AccessToken);
        }
        return msg;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"API error {(int)response.StatusCode}: {body}",
            null,
            response.StatusCode);
    }
}
