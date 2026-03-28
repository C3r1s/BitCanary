using Messenger.Application.Messages;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Application.Abstractions;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}

public interface IUserService
{
    Task<UserProfileDto> GetCurrentProfileAsync(CancellationToken cancellationToken);
    Task<UserProfileDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken);
    Task<UserSettingsDto> GetSettingsAsync(CancellationToken cancellationToken);
    Task<UserSettingsDto> UpdateSettingsAsync(UpdateSettingsRequest request, CancellationToken cancellationToken);
}

public interface IChatService
{
    Task<IReadOnlyCollection<ChatSummaryDto>> GetChatsAsync(CancellationToken cancellationToken);
    Task<ChatSummaryDto> CreateChatAsync(CreateChatRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FolderDto>> GetFoldersAsync(CancellationToken cancellationToken);
    Task<FolderDto> CreateFolderAsync(CreateFolderRequest request, CancellationToken cancellationToken);
}

public interface IMessageService
{
    Task<IReadOnlyCollection<MessageDto>> GetMessagesAsync(Guid chatId, CancellationToken cancellationToken);
    Task<MessageDto> SendAsync(SendMessageCommand command, CancellationToken cancellationToken);
}

public interface IMediaService
{
    Task<MediaUploadResponse> UploadAsync(
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content,
        CancellationToken cancellationToken);

    Task<MediaDownloadResult> DownloadAsync(Guid mediaId, CancellationToken cancellationToken);
}

public sealed record MediaDownloadResult(Stream Content, string ContentType, string FileName);

public interface ICallService
{
    Task RelaySignalAsync(CallSignalDto signal, CancellationToken cancellationToken);
}
