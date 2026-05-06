// Сервис клиента BitCanary: сеть, кэш, медиа — «IEncryptionService».
using Messenger.Client.Avalonia.Models;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.Services;

public interface IEncryptionService
{
    Task<EncryptedMessageDraft> EncryptTextAsync(string plaintext, Guid recipientUserId, CancellationToken cancellationToken = default);
    Task<string> DecryptAsync(MessageDto message, CancellationToken cancellationToken = default);
}
