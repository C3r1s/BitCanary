using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.Services.Crypto;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Messenger.Shared.Contracts.Realtime;
using SearchResult = Messenger.Client.Avalonia.Services.SearchResult;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IMessengerApiClient _apiClient;
    private readonly IRealtimeClient _realtimeClient;
    private readonly ILocalCacheService _localCacheService;
    private readonly IEncryptionService _encryptionService;
    private readonly IThemeService _themeService;
    private readonly IClientSessionService _sessionService;
    private readonly KeyPublicationService _keyPublicationService;
    private readonly ISafetyNumberService _safetyNumberService;
    private readonly IRatchetSessionRepository _sessionRepository;
    private readonly HashSet<string> _blockedSessions = new();
    private readonly Dictionary<Guid, List<MessageDto>> _messageCache = new();
    private readonly ILocalSearchService _localSearchService;
    private readonly ILocalMessageRepository _localMessageRepository;
    private readonly INotificationService _notificationService;
    private Guid? _pendingNavigationChatId;
    private readonly Dictionary<Guid, MessageItemViewModel> _outgoingVmByClientId = new();

    [ObservableProperty]
    private string _statusMessage = "Loading local cache...";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConnected;

    /// <summary>True once the user has authenticated and the main UI should be shown.</summary>
    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isMigrating;

    [ObservableProperty]
    private bool _isShowingSafetyNumber;

    [ObservableProperty]
    private bool _isShowingSettings;

    [ObservableProperty]
    private ConnectionState _connectionState;

    [ObservableProperty]
    private bool _isOfflineBannerDismissed;

    private DispatcherTimer? _offlineBannerTimer;

    /// <summary>True when ConnectionState is Online.</summary>
    public bool IsOnline => _connectionState == Messenger.Shared.Contracts.ConnectionState.Online;

    /// <summary>True when ConnectionState is Reconnecting.</summary>
    public bool IsReconnecting => _connectionState == Messenger.Shared.Contracts.ConnectionState.Reconnecting;

    /// <summary>True when ConnectionState is Offline.</summary>
    public bool IsOffline => _connectionState == Messenger.Shared.Contracts.ConnectionState.Offline;

    /// <summary>True when offline banner should be shown (offline and not dismissed).</summary>
    public bool ShowOfflineBanner => IsOffline && !IsOfflineBannerDismissed;

    /// <summary>True when no chat is currently selected.</summary>
    public bool NoChatSelected => ChatList.SelectedChat is null;

    partial void OnConnectionStateChanged(Messenger.Shared.Contracts.ConnectionState value)
    {
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(ShowOfflineBanner));

        // Stop the reappear timer if we go online
        if (value != Messenger.Shared.Contracts.ConnectionState.Offline)
        {
            _offlineBannerTimer?.Stop();
            _offlineBannerTimer = null;
            IsOfflineBannerDismissed = false;
        }
    }

    partial void OnIsOfflineBannerDismissedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOfflineBanner));
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsShowingSettings = !IsShowingSettings;
    }

    [RelayCommand]
    private void DismissOfflineBanner()
    {
        IsOfflineBannerDismissed = true;

        // Reappear after 30s if still offline
        _offlineBannerTimer?.Stop();
        _offlineBannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _offlineBannerTimer.Tick += (_, _) =>
        {
            _offlineBannerTimer?.Stop();
            _offlineBannerTimer = null;
            if (IsOffline)
            {
                IsOfflineBannerDismissed = false;
            }
        };
        _offlineBannerTimer.Start();
    }

    public SafetyNumberViewModel SafetyNumber { get; }

    public IRelayCommand ShowSafetyNumberCommand { get; }

    public IRelayCommand ToggleGlobalSearchCommand { get; }

    public ChatListViewModel ChatList { get; }

    public ChatWindowViewModel ChatWindow { get; } = new();

    public MessageInputViewModel MessageInput { get; }

    public SettingsViewModel Settings { get; }

    public LoginViewModel LoginVm { get; }

    public IAsyncRelayCommand InitializeCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand LogoutCommand { get; }

    public IRelayCommand ExitCommand { get; }

    public MainWindowViewModel(
        IMessengerApiClient apiClient,
        IRealtimeClient realtimeClient,
        ILocalCacheService localCacheService,
        IEncryptionService encryptionService,
        IThemeService themeService,
        IClientSessionService sessionService,
        KeyPublicationService keyPublicationService,
        ISafetyNumberService safetyNumberService,
        IRatchetSessionRepository sessionRepository,
        IIdentityKeyChangeDetector changeDetector,
        ILocalSearchService localSearchService,
        ILocalMessageRepository localMessageRepository,
        INotificationService notificationService)
    {
        _apiClient = apiClient;
        _realtimeClient = realtimeClient;
        _localCacheService = localCacheService;
        _encryptionService = encryptionService;
        _themeService = themeService;
        _sessionService = sessionService;
        _keyPublicationService = keyPublicationService;
        _safetyNumberService = safetyNumberService;
        _sessionRepository = sessionRepository;
        _localSearchService = localSearchService;
        _localMessageRepository = localMessageRepository;
        _notificationService = notificationService;

        SafetyNumber = new SafetyNumberViewModel(
            _safetyNumberService,
            _sessionRepository,
            () => IsShowingSafetyNumber = false);

        ChatList = new ChatListViewModel(RefreshRemoteDataAsync);

        // Wire global search
        var searchVm = new SearchViewModel(_localSearchService, NavigateToSearchResult);
        ChatList.Search = searchVm;
        ToggleGlobalSearchCommand = new RelayCommand(() => ChatList.ToggleSearchCommand.Execute(null));

        ShowSafetyNumberCommand = new RelayCommand(
            () => _ = ShowSafetyNumberAsync(),
            () => ChatList.SelectedChat is not null);

        ChatList.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName == nameof(ChatListViewModel.SelectedChat))
            {
                ShowSafetyNumberCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(NoChatSelected));
                Settings.CanShowSafetyNumber = ChatList.SelectedChat is not null;
                await LoadSelectedChatAsync();
            }
        };

        // Wire ShowSafetyNumberCommand into ChatWindow so header can bind to it
        ChatWindow.ShowSafetyNumberCommand = ShowSafetyNumberCommand;

        MessageInput = new MessageInputViewModel(
            SendMessageAsync,
            SendTypingAsync,
            () => ChatList.SelectedChat?.Id,
            () => _blockedSessions.Contains(GetCurrentSessionId()));
        Settings = new SettingsViewModel(
            ChangeThemeAsync,
            _keyPublicationService,
            scheme => _themeService.ApplyTerminalScheme(scheme));
        Settings.ShowSafetyNumberCommand = ShowSafetyNumberCommand;
        RefreshCommand = new AsyncRelayCommand(RefreshRemoteDataAsync);
        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        LogoutCommand = new RelayCommand(Logout);
        Settings.LogoutCommand = LogoutCommand;
        ExitCommand = new RelayCommand(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            {
                lifetime.Shutdown();
            }
        });

        LoginVm = new LoginViewModel(apiClient, sessionService);
        LoginVm.LoginSucceeded += HandleLoginSucceededAsync;

        _realtimeClient.MessageReceived += HandleIncomingMessageAsync;
        _realtimeClient.TypingReceived += HandleTypingAsync;
        _realtimeClient.PresenceChanged += HandlePresenceChangedAsync;
        _realtimeClient.OtpkSupplyLow += HandleOtpkSupplyLowAsync;
        _realtimeClient.MessageDelivered += HandleMessageDeliveredAsync;
        _realtimeClient.MessagesRead += HandleMessagesReadAsync;

        changeDetector.IdentityKeyChanged += HandleIdentityKeyChangedAsync;
    }

    private void NavigateToSearchResult(SearchResultItemViewModel result)
    {
        // Close global search mode
        ChatList.IsSearchMode = false;
        ChatList.Search?.Reset();

        // Find and select the chat matching result.ChatId
        var chatItem = ChatList.Chats.FirstOrDefault(c => c.Id == result.ChatId);
        if (chatItem is not null)
        {
            ChatList.SelectedChat = chatItem;
        }
        // v1: navigating to the chat is sufficient; scrolling to specific message is a stretch goal
    }

    /// <summary>
    /// Navigates to the chat matching <paramref name="chatId"/>.
    /// If the chat list is not yet populated (cold-start toast activation),
    /// stores the id as a pending navigation and retries after initialization.
    /// </summary>
    public void NavigateToChatAsync(Guid chatId)
    {
        if (ChatList.Chats.Count == 0)
        {
            // Cold-start guard (RESEARCH.md Pitfall 3): chats not loaded yet.
            _pendingNavigationChatId = chatId;
            return;
        }

        var chatItem = ChatList.Chats.FirstOrDefault(c => c.Id == chatId);
        if (chatItem is not null)
        {
            ChatList.SelectedChat = chatItem;
            IsShowingSettings = false;
        }
    }

    private void Logout()
    {
        _sessionService.ClearSession();
        ChatList.Chats.Clear();
        ChatWindow.Messages.Clear();
        _messageCache.Clear();
        IsLoggedIn = false;
        StatusMessage = "Loading local cache...";
        Settings.ConnectionStatus = "Please sign in to connect.";
        LoginVm.UserName = string.Empty;
        LoginVm.Password = string.Empty;
        LoginVm.ErrorMessage = string.Empty;
    }

    private async Task HandleLoginSucceededAsync(AuthResponse auth)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            IsLoggedIn = true;
            StatusMessage = $"Welcome, {auth.DisplayName}!";
            await ConnectAndLoadAsync();
        });
    }

    private async Task InitializeAsync()
    {
        IsBusy = true;

        try
        {
            await LoadCachedSettingsAsync();
            await LoadCachedChatsAsync();

            if (_sessionService.IsAuthenticated)
            {
                IsLoggedIn = true;
                await ConnectAndLoadAsync();
            }
            else
            {
                IsLoggedIn = false;
                Settings.ConnectionStatus = "Please sign in to connect.";
            }
        }
        finally
        {
            IsBusy = false;
        }

        // Flush cold-start pending navigation (RESEARCH.md Pitfall 3)
        if (_pendingNavigationChatId.HasValue)
        {
            var pending = _pendingNavigationChatId.Value;
            _pendingNavigationChatId = null;
            NavigateToChatAsync(pending);
        }
    }

    /// <summary>Connect SignalR, publish encryption keys, sync chats, and update status. Called after login or on startup.</summary>
    private async Task ConnectAndLoadAsync()
    {
        // Ensure encryption keys are published before connecting (D-07)
        StatusMessage = "Checking encryption keys...";
        try
        {
            await _keyPublicationService.EnsureKeyBundlePublishedAsync();
            StatusMessage = string.Empty;
            Settings.RefreshSpkRotationDate();
        }
        catch (Exception)
        {
            StatusMessage = "Key upload failed -- messages may use legacy encryption";
        }

        try
        {
            await _realtimeClient.ConnectAsync();
            IsConnected = true;
            Settings.ConnectionStatus = $"Connected · {_sessionService.ApiBaseUrl}";
            await RefreshRemoteDataAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Token is likely expired or invalid. Force logout.
            Logout();
            StatusMessage = "Your session has expired. Please log in again.";
        }
    }

    private async Task HandleOtpkSupplyLowAsync()
    {
        await _keyPublicationService.ReplenishOtpksAsync();
    }

    private async Task HandleIdentityKeyChangedAsync(string sessionId, byte[] newIkPublic)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 1. Add sessionId to blocked set
            _blockedSessions.Add(sessionId);

            // 2. Update MessageInput block state if current chat matches
            var currentSessionId = GetCurrentSessionId();
            if (currentSessionId == sessionId)
            {
                MessageInput.IsBlockedForKeyVerification = true;
                MessageInput.NotifyBlockStateChanged();
            }

            // 3. Inject banner into ChatWindow.Messages if current chat matches
            if (currentSessionId == sessionId)
            {
                var banner = MessageItemViewModel.CreateBanner(
                    "Verify before trusting new messages. If you did not expect this change, do not send sensitive information.",
                    new RelayCommand(() => _ = ShowSafetyNumberAsync()),
                    new RelayCommand(() => DismissKeyChangeBanner(sessionId)));
                ChatWindow.Messages.Add(banner);
            }
        });
    }

    private void DismissKeyChangeBanner(string sessionId)
    {
        _blockedSessions.Remove(sessionId);

        // If this is the current chat, re-enable send
        if (GetCurrentSessionId() == sessionId)
        {
            MessageInput.IsBlockedForKeyVerification = false;
            MessageInput.NotifyBlockStateChanged();
        }

        // Do NOT mark session as verified — "Send Anyway" explicitly does not verify (per UI-SPEC)
    }

    private async Task RefreshRemoteDataAsync()
    {
        if (!_sessionService.IsAuthenticated)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Synchronizing chats...";

        try
        {
            var chats = await _apiClient.GetChatsAsync();
            await ApplyChatSummariesAsync(chats);
            await _localCacheService.SaveAsync("chats", chats);

            var settings = await _apiClient.GetSettingsAsync();
            ApplySettings(settings);
            await _localCacheService.SaveAsync("settings", settings);

            StatusMessage = $"Synced {chats.Count} chats.";

            // Index all messages in background so FTS5 search works across all chats,
            // not just ones the user has manually opened.
            _ = Task.Run(IndexAllChatsForSearchAsync);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Fetches messages for every chat and runs them through DecryptAsync so
    /// plaintext_body is written to SQLite and FTS5 picks them up.
    /// Runs fire-and-forget after RefreshRemoteDataAsync.
    /// </summary>
    private async Task IndexAllChatsForSearchAsync()
    {
        if (!_sessionService.IsAuthenticated) return;

        foreach (var chat in ChatList.Chats.ToList())
        {
            try
            {
                var messages = await _apiClient.GetMessagesAsync(chat.Id);
                foreach (var msg in messages)
                {
                    try { await _encryptionService.DecryptAsync(msg); }
                    catch { /* ignore individual decrypt errors */ }
                }
            }
            catch { /* ignore API errors (e.g. offline) */ }
        }
    }

    private async Task LoadSelectedChatAsync()
    {
        var selectedChat = ChatList.SelectedChat;
        ChatWindow.Messages.Clear();
        ChatWindow.TypingStatus = string.Empty;

        if (selectedChat is null)
        {
            ChatWindow.Title = "Select a chat";
            ChatWindow.Subtitle = "Choose a conversation to view encrypted history.";
            ChatWindow.IsSessionVerified = false;
            MessageInput.NotifyBlockStateChanged();
            return;
        }

        ChatWindow.Title = selectedChat.Title;
        ChatWindow.Subtitle = selectedChat.Type.ToString();
        StatusMessage = $"Loading {selectedChat.Title}...";

        // Reset unread count when opening chat (per D-07)
        selectedChat.UnreadCount = 0;
        await _localMessageRepository.ResetUnreadCountAsync(selectedChat.Id);

        // Load verification state for this session
        var sessionId = GetCurrentSessionId();
        if (!string.IsNullOrEmpty(sessionId))
        {
            var verState = await _sessionRepository.LoadVerificationStateAsync(sessionId);
            ChatWindow.IsSessionVerified = verState.Verified;

            // Re-evaluate block state per session (Pitfall 4: must not share block state across chats)
            MessageInput.IsBlockedForKeyVerification = _blockedSessions.Contains(sessionId);
        }
        else
        {
            ChatWindow.IsSessionVerified = false;
            MessageInput.IsBlockedForKeyVerification = false;
        }

        // Notify message input so CanSend re-evaluates for the new chat
        MessageInput.NotifyBlockStateChanged();

        var cacheKey = GetMessageCacheKey(selectedChat.Id);
        var cachedMessages = await _localCacheService.LoadAsync<IReadOnlyCollection<MessageDto>>(cacheKey);
        if (cachedMessages is not null)
        {
            await ReplaceMessagesAsync(selectedChat.Id, cachedMessages);
        }

        if (_sessionService.IsAuthenticated)
        {
            try
            {
                var messages = await _apiClient.GetMessagesAsync(selectedChat.Id);
                await ReplaceMessagesAsync(selectedChat.Id, messages);
                await _localCacheService.SaveAsync(cacheKey, messages);
                await _realtimeClient.JoinChatAsync(selectedChat.Id);
                // Notify senders that their messages have been read (D-03, D-07)
                await _realtimeClient.SendReadReceiptAsync(selectedChat.Id);
                StatusMessage = $"{selectedChat.Title} ready.";
            }
            catch (System.Net.Http.HttpRequestException)
            {
                // Server unreachable — show cached messages and continue
                StatusMessage = $"{selectedChat.Title} (offline — showing cached messages)";
            }
        }
        else
        {
            StatusMessage = $"{selectedChat.Title} ready.";
        }
    }

    private async Task ShowSafetyNumberAsync()
    {
        var selectedChat = ChatList.SelectedChat;
        if (selectedChat is null) return;

        var bundle = await _apiClient.GetKeyBundleAsync(selectedChat.PeerUserId);
        if (bundle is null) return;

        var sessionId = GetCurrentSessionId();
        byte[] localIkPublic;
        try
        {
            localIkPublic = _keyPublicationService.LocalBundle.IkPublic;
        }
        catch (InvalidOperationException)
        {
            // Key bundle not yet loaded — can't show safety number
            return;
        }

        await SafetyNumber.LoadAsync(
            sessionId,
            selectedChat.Title,
            localIkPublic,
            _sessionService.CurrentUserId.ToString("D"),
            bundle.IkPublic,
            selectedChat.PeerUserId.ToString("D"));

        IsShowingSafetyNumber = true;
    }

    private string GetCurrentSessionId()
    {
        var selected = ChatList.SelectedChat;
        if (selected is null) return string.Empty;
        return $"{_sessionService.CurrentUserId}:{selected.PeerUserId}";
    }

    private async Task SendMessageAsync(string plaintext)
    {
        var selectedChat = ChatList.SelectedChat;
        if (selectedChat is null)
        {
            return;
        }

        if (!_sessionService.IsAuthenticated)
        {
            var demoMessage = new MessageDto(
                Guid.NewGuid(),
                selectedChat.Id,
                Guid.Empty,
                _sessionService.UserName,
                MessageKind.Text,
                plaintext,
                "plaintext-demo",
                "demo-envelope",
                null,
                null,
                null,
                DateTimeOffset.UtcNow);

            await AppendMessageAsync(demoMessage);
            return;
        }

        var clientMessageId = Guid.NewGuid();
        var recipientUserId = selectedChat.PeerUserId;
        var encrypted = await _encryptionService.EncryptTextAsync(plaintext, recipientUserId);
        var request = new SendMessageRequest(
            selectedChat.Id,
            clientMessageId,
            encrypted.Kind,
            encrypted.EncryptedPayload,
            encrypted.EncryptionAlgorithm,
            encrypted.KeyEnvelope,
            null,
            null,
            encrypted.MetadataJson,
            ProtocolVersion.SignalProtocol);

        // D-04: Create optimistic VM and display immediately before the POST
        var optimisticVm = new MessageItemViewModel
        {
            Id = Guid.Empty,  // No server ID yet
            ClientMessageId = clientMessageId,
            SenderDisplayName = _sessionService.UserName,
            DisplayText = plaintext,
            Timestamp = DateTimeOffset.UtcNow.LocalDateTime.ToShortTimeString(),
            IsOutgoing = true,
            Status = MessageStatus.Sending
        };
        optimisticVm.SetRetryDelegate(RetryMessageAsync);
        _outgoingVmByClientId[clientMessageId] = optimisticVm;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ChatList.SelectedChat?.Id == selectedChat.Id)
            {
                ChatWindow.Messages.Add(optimisticVm);
            }
        });

        try
        {
            var message = await _apiClient.SendMessageAsync(request);

            // Remove tracking entry — server confirmed
            _outgoingVmByClientId.Remove(clientMessageId);

            // D-08: Only persist server-confirmed messages to SQLite
            await AppendMessageAsync(message);
            await PersistMessagesAsync(selectedChat.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TaskCanceledException)
        {
            // D-04/D-05: Mark the in-flight VM as failed — no SQLite write (D-08)
            _outgoingVmByClientId.Remove(clientMessageId);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                optimisticVm.Status = MessageStatus.Failed;
            });
        }
    }

    /// <summary>
    /// Re-sends a failed message using the same ClientMessageId.
    /// Server-side dedup on ClientMessageId makes this safe even if the original
    /// POST reached the server — the second call is a no-op on the server.
    /// Per D-06: immediate retry, no queuing.
    /// </summary>
    private async Task RetryMessageAsync(Guid clientMessageId)
    {
        // Find the failed optimistic VM by ClientMessageId
        var vm = ChatWindow.Messages
            .OfType<MessageItemViewModel>()
            .FirstOrDefault(m => m.ClientMessageId == clientMessageId);
        if (vm is null) return;

        var selectedChat = ChatList.SelectedChat;
        if (selectedChat is null) return;

        var plaintext = vm.DisplayText;
        var recipientUserId = selectedChat.PeerUserId;
        var encrypted = await _encryptionService.EncryptTextAsync(plaintext, recipientUserId);

        var request = new SendMessageRequest(
            selectedChat.Id,
            clientMessageId,  // Same Guid — server deduplicates
            encrypted.Kind,
            encrypted.EncryptedPayload,
            encrypted.EncryptionAlgorithm,
            encrypted.KeyEnvelope,
            null,
            null,
            encrypted.MetadataJson,
            ProtocolVersion.SignalProtocol);

        try
        {
            var message = await _apiClient.SendMessageAsync(request);
            // Success — persist server-confirmed message
            await AppendMessageAsync(message);
            await PersistMessagesAsync(selectedChat.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TaskCanceledException)
        {
            // Still offline / failed — revert VM status to Failed
            // (ExecuteRetryAsync set it to Sending before calling here)
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                vm.Status = MessageStatus.Failed;
            });
        }
    }

    private Task SendTypingAsync(Guid? chatId, bool isTyping)
    {
        if (chatId is null || !_sessionService.IsAuthenticated)
        {
            return Task.CompletedTask;
        }

        return _realtimeClient.SendTypingIndicatorAsync(chatId.Value, isTyping);
    }

    private async Task ChangeThemeAsync(ThemePreference themePreference)
    {
        _themeService.Apply(themePreference);

        var settings = new UpdateSettingsRequest(
            themePreference,
            Settings.SendByEnter,
            Settings.UseCompactMode,
            Settings.EnableCustomEmoji,
            Settings.ShowNotifications,
            Settings.ShowSenderName);

        if (_sessionService.IsAuthenticated)
        {
            await _apiClient.UpdateSettingsAsync(settings);
        }

        await _localCacheService.SaveAsync("settings", new UserSettingsDto(
            themePreference,
            Settings.SendByEnter,
            Settings.UseCompactMode,
            Settings.EnableCustomEmoji,
            Settings.ShowNotifications,
            Settings.ShowSenderName));
    }

    private async Task HandleIncomingMessageAsync(MessageDto message)
    {
        ChatListItemViewModel? chatItem = null;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await UpdateChatPreviewAsync(message);

            if (ChatList.SelectedChat?.Id == message.ChatId)
            {
                await AppendMessageToWindowAsync(message);
            }

            chatItem = ChatList.Chats.FirstOrDefault(c => c.Id == message.ChatId);
        });

        // D-04/D-05 (CONTEXT.md): ShowIfMinimized guards internally on WindowState == Minimized.
        _notificationService.ShowIfMinimized(message.ChatId, chatItem?.Title);

        await PersistMessagesAsync(message.ChatId);
    }

    private async Task HandleTypingAsync(TypingIndicatorDto typing)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ChatList.SelectedChat?.Id == typing.ChatId)
            {
                ChatWindow.TypingStatus = typing.IsTyping
                    ? $"{typing.DisplayName} is typing..."
                    : string.Empty;
            }
        });
    }

    private async Task HandlePresenceChangedAsync(PresenceChangedDto presenceChanged)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            StatusMessage = presenceChanged.IsOnline
                ? $"{presenceChanged.UserId} is online."
                : $"{presenceChanged.UserId} last seen at {presenceChanged.LastSeenUtc:t}.";
        });
    }

    private async Task HandleMessageDeliveredAsync(Guid messageId)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var message = ChatWindow.Messages.FirstOrDefault(m => m.Id == messageId);
            if (message is not null && message.Status < MessageStatus.Delivered)
            {
                message.Status = MessageStatus.Delivered;
            }
        });

        // Persist the status update to SQLite
        await _localMessageRepository.UpdateMessageStatusAsync(messageId, MessageStatus.Delivered);
    }

    private async Task HandleMessagesReadAsync(Guid chatId, Guid readByUserId)
    {
        // Don't update status if the reader is the current user (their own messages aren't "read" by themselves)
        if (readByUserId == _sessionService.CurrentUserId) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ChatList.SelectedChat?.Id == chatId)
            {
                foreach (var message in ChatWindow.Messages)
                {
                    if (message.IsOutgoing && message.Status < MessageStatus.Read)
                    {
                        message.Status = MessageStatus.Read;
                    }
                }
            }
        });

        // Persist all outgoing messages as read in SQLite
        await _localMessageRepository.MarkMessagesReadAsync(chatId, _sessionService.CurrentUserId);
    }

    private async Task LoadCachedSettingsAsync()
    {
        var settings = await _localCacheService.LoadAsync<UserSettingsDto>("settings");
        if (settings is not null)
        {
            ApplySettings(settings);
        }
    }

    private async Task LoadCachedChatsAsync()
    {
        var chats = await _localCacheService.LoadAsync<IReadOnlyCollection<ChatSummaryDto>>("chats");
        if (chats is not null)
        {
            await ApplyChatSummariesAsync(chats);
        }
    }

    private void ApplySettings(UserSettingsDto settings)
    {
        _themeService.Apply(settings.ThemePreference);
        Settings.SelectedThemeOption = Settings.ThemeOptions.FirstOrDefault(x => x.Value == settings.ThemePreference)
                                    ?? Settings.ThemeOptions[0];
        Settings.SendByEnter = settings.SendByEnter;
        Settings.UseCompactMode = settings.UseCompactMode;
        Settings.EnableCustomEmoji = settings.EnableCustomEmoji;
        Settings.ShowNotifications = settings.ShowNotifications;
        Settings.ShowSenderName = settings.ShowSenderName;
    }

    private async Task ApplyChatSummariesAsync(IReadOnlyCollection<ChatSummaryDto> chats)
    {
        ChatList.Chats.Clear();

        foreach (var chat in chats)
        {
            // Persist chat to SQLite so FTS5 search JOIN resolves chat names
            await _localMessageRepository.UpsertChatAsync(chat);

            // Determine peer for 1-to-1 chats (used for E2E encryption recipient)
            var peer = chat.Members?.FirstOrDefault(m => m.UserId != _sessionService.CurrentUserId);

            string subtitle;
            if (chat.LastMessage is null)
            {
                subtitle = "No messages yet";
            }
            else
            {
                try
                {
                    subtitle = await _encryptionService.DecryptAsync(chat.LastMessage);
                }
                catch
                {
                    subtitle = "[Unable to decrypt]";
                }
            }

            var lastActivity = chat.LastMessage is not null
                ? TimestampFormatter.FormatTimestamp(chat.LastMessage.CreatedAtUtc)
                : string.Empty;

            ChatList.Chats.Add(new ChatListItemViewModel
            {
                Id = chat.Id,
                Title = chat.Title,
                Type = chat.Type,
                PeerUserId = peer?.UserId ?? Guid.Empty,
                Subtitle = subtitle,
                LastActivity = lastActivity,
                UnreadCount = chat.UnreadCount
            });
        }

        ChatList.SelectedChat ??= ChatList.Chats.FirstOrDefault();
    }

    private async Task ReplaceMessagesAsync(Guid chatId, IReadOnlyCollection<MessageDto> messages)
    {
        _messageCache[chatId] = messages.ToList();
        ChatWindow.Messages.Clear();

        foreach (var message in messages)
        {
            await AppendMessageToWindowAsync(message);
        }
    }

    private async Task AppendMessageAsync(MessageDto message)
    {
        if (!_messageCache.TryGetValue(message.ChatId, out var messages))
        {
            messages = new List<MessageDto>();
            _messageCache[message.ChatId] = messages;
        }

        if (messages.Any(x => x.Id == message.Id))
        {
            return;
        }

        messages.Add(message);

        if (ChatList.SelectedChat?.Id == message.ChatId)
        {
            await AppendMessageToWindowAsync(message);
        }
    }

    private async Task AppendMessageToWindowAsync(MessageDto message)
    {
        if (ChatWindow.Messages.Any(x => x.Id == message.Id))
        {
            return;
        }

        string displayText;
        try
        {
            displayText = await _encryptionService.DecryptAsync(message);
        }
        catch
        {
            displayText = "[Unable to decrypt]";
        }

        ChatWindow.Messages.Add(new MessageItemViewModel
        {
            Id = message.Id,
            ClientMessageId = Guid.NewGuid(),
            SenderDisplayName = message.SenderDisplayName,
            DisplayText = displayText,
            Timestamp = message.CreatedAtUtc.LocalDateTime.ToShortTimeString(),
            IsOutgoing = message.SenderId == _sessionService.CurrentUserId
        });
    }

    private async Task UpdateChatPreviewAsync(MessageDto message)
    {
        var chatItem = ChatList.Chats.FirstOrDefault(x => x.Id == message.ChatId);
        if (chatItem is null)
        {
            return;
        }

        try
        {
            chatItem.Subtitle = await _encryptionService.DecryptAsync(message);
        }
        catch
        {
            chatItem.Subtitle = "[Unable to decrypt]";
        }

        chatItem.LastActivity = TimestampFormatter.FormatTimestamp(message.CreatedAtUtc);
    }

    private Task PersistMessagesAsync(Guid chatId)
    {
        if (!_messageCache.TryGetValue(chatId, out var messages))
        {
            return Task.CompletedTask;
        }

        return _localCacheService.SaveAsync(GetMessageCacheKey(chatId), messages);
    }

    private static string GetMessageCacheKey(Guid chatId) => $"messages-{chatId:N}";
}
