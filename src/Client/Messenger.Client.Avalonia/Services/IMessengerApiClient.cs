// Сервис клиента BitCanary: сеть, кэш, медиа — «IMessengerApiClient».
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.Services;

public interface IMessengerApiClient
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ChatSummaryDto>> GetChatsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MessageDto>> GetMessagesAsync(Guid chatId, CancellationToken cancellationToken = default);
    Task<MessageDto> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);
    Task DeleteChatAsync(Guid chatId, CancellationToken cancellationToken = default);

    Task<UserSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<UserSettingsDto> UpdateSettingsAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default);

    Task<MediaUploadResponse> UploadMediaAsync(
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<byte[]> DownloadMediaAsync(Guid mediaId, CancellationToken cancellationToken = default);

    Task<KeyBundleDto?> GetKeyBundleAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<BundleUploadResponse> UploadKeyBundleAsync(KeyBundleUploadRequest request, CancellationToken cancellationToken = default);
    Task<OtpkReplenishResponse> ReplenishOtpksAsync(OtpkReplenishRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<UserProfileDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
    Task<ChatSummaryDto> CreateChatAsync(CreateChatRequest request, CancellationToken cancellationToken = default);

    Task<ChatSummaryDto> AddMemberAsync(Guid chatId, Guid userId, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(Guid chatId, Guid userId, CancellationToken cancellationToken = default);
    Task UpdateMemberRoleAsync(Guid chatId, Guid userId, ChatRole role, CancellationToken cancellationToken = default);
    Task<ChatSummaryDto> UpdateChatAsync(Guid chatId, UpdateChatRequest request, CancellationToken cancellationToken = default);
}
