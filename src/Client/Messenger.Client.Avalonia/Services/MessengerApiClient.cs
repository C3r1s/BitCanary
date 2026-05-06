// Сервис клиента BitCanary: сеть, кэш, медиа — «MessengerApiClient».
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.Services;

public sealed class MessengerApiClient(IClientSessionService sessionService) : IMessengerApiClient
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri($"{sessionService.ApiBaseUrl.TrimEnd('/')}/")
    };


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

    public async Task DeleteChatAsync(Guid chatId, CancellationToken cancellationToken = default)
    {
        using var req = Authorized(HttpMethod.Delete, $"api/chats/{chatId}");
        var response = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }


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

    public async Task<byte[]> DownloadMediaAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        using var req = Authorized(HttpMethod.Get, $"api/media/{mediaId}");
        var response = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }


    public async Task<KeyBundleDto?> GetKeyBundleAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        using var req = Authorized(HttpMethod.Get, $"api/keys/{userId}");
        var response = await _http.SendAsync(req, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<KeyBundleDto>(cancellationToken);
    }

    public async Task<BundleUploadResponse> UploadKeyBundleAsync(KeyBundleUploadRequest request, CancellationToken cancellationToken = default)
    {
        using var req = Authorized(HttpMethod.Post, "api/keys/bundle");
        req.Content = JsonContent.Create(request);
        var response = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<BundleUploadResponse>(cancellationToken))!;
    }

    public async Task<OtpkReplenishResponse> ReplenishOtpksAsync(OtpkReplenishRequest request, CancellationToken cancellationToken = default)
    {
        using var req = Authorized(HttpMethod.Post, "api/keys/opk/batch");
        req.Content = JsonContent.Create(request);
        var response = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<OtpkReplenishResponse>(cancellationToken))!;
    }


    public async Task<IReadOnlyCollection<UserProfileDto>> SearchUsersAsync(
        string query, CancellationToken cancellationToken = default)
    {
        if (!sessionService.IsAuthenticated) return Array.Empty<UserProfileDto>();
        using var req = Authorized(HttpMethod.Get, $"api/users/search?q={Uri.EscapeDataString(query)}");
        var response = await _http.SendAsync(req, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<UserProfileDto>>(cancellationToken)
               ?? Array.Empty<UserProfileDto>();
    }

    public async Task<ChatSummaryDto> CreateChatAsync(
        CreateChatRequest request, CancellationToken cancellationToken = default)
    {
        using var req = Authorized(HttpMethod.Post, "api/chats");
        req.Content = JsonContent.Create(request);
        var response = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ChatSummaryDto>(cancellationToken))!;
    }


    public async Task<ChatSummaryDto> AddMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        using var req = Authorized(HttpMethod.Post, $"api/chats/{chatId}/members");
        req.Content = JsonContent.Create(new { UserId = userId });
        var response = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<ChatSummaryDto>(ct))!;
    }

    public async Task RemoveMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        using var req = Authorized(HttpMethod.Delete, $"api/chats/{chatId}/members/{userId}");
        var response = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task UpdateMemberRoleAsync(Guid chatId, Guid userId, ChatRole role, CancellationToken ct = default)
    {
        using var req = Authorized(HttpMethod.Patch, $"api/chats/{chatId}/members/{userId}/role");
        req.Content = JsonContent.Create(new { Role = role });
        var response = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<ChatSummaryDto> UpdateChatAsync(Guid chatId, UpdateChatRequest request, CancellationToken ct = default)
    {
        using var req = Authorized(HttpMethod.Patch, $"api/chats/{chatId}");
        req.Content = JsonContent.Create(request);
        var response = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<ChatSummaryDto>(ct))!;
    }


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
