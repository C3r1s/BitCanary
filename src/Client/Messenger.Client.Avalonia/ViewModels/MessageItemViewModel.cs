namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class MessageItemViewModel : ViewModelBase
{
    public required Guid Id { get; init; }
    public required string SenderDisplayName { get; init; }
    public required string DisplayText { get; init; }
    public required string Timestamp { get; init; }
    public required bool IsOutgoing { get; init; }
}
