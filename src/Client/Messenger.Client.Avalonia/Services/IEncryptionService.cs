using Messenger.Client.Avalonia.Models;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.Services;

public interface IEncryptionService
{
    Task<EncryptedMessageDraft> EncryptTextAsync(string plaintext, CancellationToken cancellationToken = default);
    string TryDecrypt(MessageDto message);
}
