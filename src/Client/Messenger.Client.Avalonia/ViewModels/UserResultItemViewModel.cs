// Состояние и команды UI BitCanary для «UserResultItemViewModel».
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed class UserResultItemViewModel
{
    public UserResultItemViewModel(UserProfileDto dto)
    {
        Dto = dto;
        UserId = dto.Id;
        DisplayName = dto.DisplayName;
        Username = $"@{dto.UserName}";
    }

    public UserProfileDto Dto { get; }
    public Guid UserId { get; }
    public string DisplayName { get; }
    public string Username { get; }
}
