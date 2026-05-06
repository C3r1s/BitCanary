// Состояние и команды UI BitCanary для «SafetyNumberViewModel».
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Services.Crypto;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class SafetyNumberViewModel : ViewModelBase
{
    private readonly IChatSafetyNumberStore _chatSafetyStore;
    private readonly IRatchetSessionRepository _sessionRepo;
    private Guid _userId;
    private Guid _chatId;
    private string _sessionId = string.Empty;
    private byte[]? _remoteIkPublic;

    [ObservableProperty]
    private string _safetyNumber = string.Empty;

    [ObservableProperty]
    private string _peerDisplayName = string.Empty;

    [ObservableProperty]
    private bool _isVerified;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _verifiedOnText = string.Empty;

    [ObservableProperty]
    private bool _showPeerDebugSafetyNumber;

    [ObservableProperty]
    private string _peerDebugSafetyNumber = string.Empty;

    [ObservableProperty]
    private bool _canRegenerate;

    [ObservableProperty]
    private bool _canMarkVerified;

    [ObservableProperty]
    private string _regenerateStatusText = string.Empty;

    public string VerificationStatusText => IsVerified ? "VERIFIED (auto)" : "UNVERIFIED";
    public string VerificationStatusColor => IsVerified ? "#00CC66" : "#E05050";

    public IRelayCommand CloseCommand { get; }
    public IRelayCommand TogglePeerDebugSafetyNumberCommand { get; }
    public IAsyncRelayCommand RegenerateSafetyNumberCommand { get; }
    public IAsyncRelayCommand MarkVerifiedCommand { get; }

    public SafetyNumberViewModel(
        IChatSafetyNumberStore chatSafetyStore,
        IRatchetSessionRepository sessionRepo,
        Action closeOverlay)
    {
        _chatSafetyStore = chatSafetyStore;
        _sessionRepo = sessionRepo;
        CloseCommand = new RelayCommand(closeOverlay);
        TogglePeerDebugSafetyNumberCommand = new RelayCommand(() =>
        {
            ShowPeerDebugSafetyNumber = !ShowPeerDebugSafetyNumber;
        });
        RegenerateSafetyNumberCommand = new AsyncRelayCommand(RegenerateSafetyNumberAsync, () => CanRegenerate);
        MarkVerifiedCommand = new AsyncRelayCommand(MarkVerifiedAsync, () => CanMarkVerified);
    }

    partial void OnIsVerifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(VerificationStatusText));
        OnPropertyChanged(nameof(VerificationStatusColor));
    }

    partial void OnCanRegenerateChanged(bool value)
    {
        RegenerateSafetyNumberCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanMarkVerifiedChanged(bool value)
    {
        MarkVerifiedCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync(
        Guid userId,
        Guid chatId,
        bool canRegenerate,
        string sessionId,
        string peerDisplayName)
    {
        IsLoading = true;
        _userId = userId;
        _chatId = chatId;
        _sessionId = sessionId ?? string.Empty;
        CanRegenerate = canRegenerate;
        RegenerateStatusText = string.Empty;
        PeerDisplayName = peerDisplayName;
        var state = string.IsNullOrWhiteSpace(_sessionId)
            ? (Verified: false, LastVerifiedAt: (DateTimeOffset?)null, RemoteIkPublic: (byte[]?)null)
            : await _sessionRepo.LoadVerificationStateAsync(_sessionId);
        _remoteIkPublic = state.RemoteIkPublic;
        IsVerified = state.Verified;
        CanMarkVerified = !IsVerified && !string.IsNullOrWhiteSpace(_sessionId);
        VerifiedOnText = state.LastVerifiedAt.HasValue
            ? $"Verified on {state.LastVerifiedAt.Value.LocalDateTime.ToShortDateString()}"
            : string.Empty;

        SafetyNumber = await _chatSafetyStore.GetOrCreateAsync(_userId, _chatId, canRegenerate)
                       ?? "Safety number is stored only by chat owner for this chat";
        PeerDebugSafetyNumber = SafetyNumber;
        ShowPeerDebugSafetyNumber = false;
        IsLoading = false;
    }

    private async Task MarkVerifiedAsync()
    {
        if (string.IsNullOrWhiteSpace(_sessionId)) return;
        var now = DateTimeOffset.UtcNow;
        await _sessionRepo.SaveVerificationStateAsync(
            _sessionId,
            verified: true,
            lastVerifiedAt: now,
            remoteIkPublic: _remoteIkPublic);

        IsVerified = true;
        CanMarkVerified = false;
        VerifiedOnText = $"Verified on {now.LocalDateTime.ToShortDateString()}";
        RegenerateStatusText = "Session marked as VERIFIED.";
    }

    private async Task RegenerateSafetyNumberAsync()
    {
        if (!CanRegenerate) return;
        try
        {
            SafetyNumber = await _chatSafetyStore.RegenerateAsync(_userId, _chatId, true);
            PeerDebugSafetyNumber = SafetyNumber;
            RegenerateStatusText = "Safety number regenerated for this chat.";
        }
        catch (Exception ex)
        {
            RegenerateStatusText = $"Regenerate failed: {ex.Message}";
        }
    }
}
