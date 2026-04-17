using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Services.Crypto;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class SafetyNumberViewModel : ViewModelBase
{
    private readonly ISafetyNumberService _safetyNumberService;
    private readonly IRatchetSessionRepository _sessionRepo;
    private readonly Action? _onVerified;

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

    public string VerifyButtonText => IsVerified ? "Already Verified" : "Mark as Verified";

    public IAsyncRelayCommand MarkAsVerifiedCommand { get; }

    public IRelayCommand CloseCommand { get; }

    public SafetyNumberViewModel(
        ISafetyNumberService safetyNumberService,
        IRatchetSessionRepository sessionRepo,
        Action closeOverlay,
        Action? onVerified = null)
    {
        _safetyNumberService = safetyNumberService;
        _sessionRepo = sessionRepo;
        _onVerified = onVerified;

        CloseCommand = new RelayCommand(closeOverlay);
        MarkAsVerifiedCommand = new AsyncRelayCommand(MarkAsVerifiedAsync, () => !IsVerified);
    }

    partial void OnIsVerifiedChanged(bool value)
    {
        MarkAsVerifiedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(VerifyButtonText));
    }

    public async Task LoadAsync(
        string sessionId,
        string peerDisplayName,
        byte[] localIkPublic,
        string localUserId,
        byte[] remoteIkPublic,
        string remoteUserId)
    {
        IsLoading = true;
        PeerDisplayName = peerDisplayName;
        _remoteIkPublic = remoteIkPublic;
        _sessionId = sessionId;

        var state = await _sessionRepo.LoadVerificationStateAsync(sessionId);
        IsVerified = state.Verified;
        VerifiedOnText = state.LastVerifiedAt.HasValue
            ? $"Verified on {state.LastVerifiedAt.Value.LocalDateTime.ToShortDateString()}"
            : string.Empty;

        SafetyNumber = _safetyNumberService.Compute(localIkPublic, localUserId, remoteIkPublic, remoteUserId);
        IsLoading = false;
    }

    private async Task MarkAsVerifiedAsync()
    {
        if (IsVerified) return;

        var now = DateTimeOffset.UtcNow;
        await _sessionRepo.SaveVerificationStateAsync(_sessionId, verified: true, now, _remoteIkPublic);
        IsVerified = true;
        VerifiedOnText = $"Verified on {DateTime.Now.ToShortDateString()}";
        _onVerified?.Invoke();
    }
}
