// Состояние и команды UI BitCanary для «MainWindowViewModel».
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Models;
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
    private readonly IChatSafetyNumberStore _chatSafetyNumberStore;
    private readonly IRatchetSessionRepository _sessionRepository;
    private readonly HashSet<string> _blockedSessions = new();
    private readonly Dictionary<Guid, List<MessageDto>> _messageCache = new();
    private readonly ILocalSearchService _localSearchService;
    private readonly ILocalMessageRepository _localMessageRepository;
    private readonly INotificationService _notificationService;
    private readonly ISessionManager _sessionManager;
    private Guid? _pendingNavigationChatId;
    private readonly Dictionary<Guid, MessageItemViewModel> _outgoingVmByClientId = new();
    private readonly Dictionary<Guid, string> _sentPlaintextByMessageId = new();
    private readonly Dictionary<Guid, ChatSummaryDto> _chatSummaryCache = new();
    private readonly HashSet<Guid> _deletedChatIds = new();
    private readonly Dictionary<Guid, DateTimeOffset> _clearedChats = new();

    [ObservableProperty]
    private string _statusMessage = "Loading local cache...";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isMigrating;

    [ObservableProperty]
    private bool _isShowingDebugError;

    [ObservableProperty]
    private string _debugErrorText = string.Empty;

    [RelayCommand]
    private void CloseDebugError() => IsShowingDebugError = false;

    [ObservableProperty]
    private bool _isShowingSafetyNumber;

    [ObservableProperty]
    private bool _isShowingSettings;

    [ObservableProperty]
    private bool _isConfirmingDeleteChat;

    [ObservableProperty]
    private string _pendingDeleteChatTitle = string.Empty;

    private Guid? _pendingDeleteChatId;

    [ObservableProperty]
    private ConnectionState _connectionState;

    [ObservableProperty]
    private bool _isOfflineBannerDismissed;

    [ObservableProperty]
    private string _currentUsername = string.Empty;

    public bool HasCurrentUsername => !string.IsNullOrEmpty(CurrentUsername);

    partial void OnCurrentUsernameChanged(string value)
    {
        OnPropertyChanged(nameof(HasCurrentUsername));
    }

    private DispatcherTimer? _offlineBannerTimer;

    public bool IsOnline => _connectionState == Messenger.Shared.Contracts.ConnectionState.Online;

    public bool IsReconnecting => _connectionState == Messenger.Shared.Contracts.ConnectionState.Reconnecting;

    public bool IsOffline => _connectionState == Messenger.Shared.Contracts.ConnectionState.Offline;

    public bool ShowOfflineBanner => IsOffline && !IsOfflineBannerDismissed;

    public bool NoChatSelected => ChatList.SelectedChat is null;

    partial void OnConnectionStateChanged(Messenger.Shared.Contracts.ConnectionState value)
    {
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(ShowOfflineBanner));

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
    private void CancelDeleteChat()
    {
        IsConfirmingDeleteChat = false;
        PendingDeleteChatTitle = string.Empty;
        _pendingDeleteChatId = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteChatAsync()
    {
        if (_pendingDeleteChatId is null)
        {
            CancelDeleteChat();
            return;
        }

        var chatId = _pendingDeleteChatId.Value;
        CancelDeleteChat();
        await ExecuteDeleteChatAsync(chatId);
    }

    [RelayCommand]
    private void CloseActiveChat()
    {
        ChatList.SelectedChat = null;
    }

    [RelayCommand]
    private void DismissOfflineBanner()
    {
        IsOfflineBannerDismissed = true;

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
        IChatSafetyNumberStore chatSafetyNumberStore,
        IRatchetSessionRepository sessionRepository,
        IIdentityKeyChangeDetector changeDetector,
        ILocalSearchService localSearchService,
        ILocalMessageRepository localMessageRepository,
        INotificationService notificationService,
        ISessionManager sessionManager)
    {
        _apiClient = apiClient;
        _realtimeClient = realtimeClient;
        _localCacheService = localCacheService;
        _encryptionService = encryptionService;
        _themeService = themeService;
        _sessionService = sessionService;
        _keyPublicationService = keyPublicationService;
        _chatSafetyNumberStore = chatSafetyNumberStore;
        _sessionRepository = sessionRepository;
        _localSearchService = localSearchService;
        _localMessageRepository = localMessageRepository;
        _notificationService = notificationService;
        _sessionManager = sessionManager;

        SafetyNumber = new SafetyNumberViewModel(
            _chatSafetyNumberStore,
            _sessionRepository,
            () => IsShowingSafetyNumber = false);
        SafetyNumber.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SafetyNumberViewModel.IsVerified) &&
                ChatList.SelectedChat?.Type == ChatType.Direct)
            {
                ChatWindow.IsSessionVerified = SafetyNumber.IsVerified;
            }
        };

        ChatList = new ChatListViewModel(RefreshRemoteDataAsync);

        var searchVm = new SearchViewModel(
            _localSearchService,
            NavigateToSearchResult,
            (query, ct) => _apiClient.SearchUsersAsync(query, ct),
            user => _ = HandleUserSelectedAsync(user));
        ChatList.Search = searchVm;
        ToggleGlobalSearchCommand = new RelayCommand(() => ChatList.ToggleSearchCommand.Execute(null));

        var userSearchVm = new UserSearchViewModel(_apiClient, HandleUserSelectedAsync);
        ChatList.UserSearch = userSearchVm;

        var groupCreationVm = new GroupCreationViewModel(
            (query, ct) => _apiClient.SearchUsersAsync(query, ct),
            HandleGroupCreatedAsync);
        ChatList.GroupCreation = groupCreationVm;

        ShowSafetyNumberCommand = new RelayCommand(
            () => _ = ShowSafetyNumberAsync(),
            () => ChatList.SelectedChat is not null);

        ChatList.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName == nameof(ChatListViewModel.SelectedChat))
            {
                _realtimeClient.SetCurrentlySelectedChatId(ChatList.SelectedChat?.Id);
                ShowSafetyNumberCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(NoChatSelected));
                Settings.CanShowSafetyNumber = ChatList.SelectedChat is not null;
                await LoadSelectedChatAsync();
            }
        };

        ChatWindow.ShowSafetyNumberCommand = ShowSafetyNumberCommand;

        var groupInfoVm = new GroupInfoViewModel(
            (chatId, userId) => _apiClient.AddMemberAsync(chatId, userId),
            (chatId, userId) => _apiClient.RemoveMemberAsync(chatId, userId),
            (chatId, userId, role) => _apiClient.UpdateMemberRoleAsync(chatId, userId, role),
            (chatId, req) => _apiClient.UpdateChatAsync(chatId, req),
            (q, ct) => _apiClient.SearchUsersAsync(q, ct),
            () => ChatWindow.IsGroupInfoVisible = false);
        ChatWindow.GroupInfo = groupInfoVm;
        ChatWindow.ShowGroupInfoCommand = new RelayCommand(() => ChatWindow.IsGroupInfoVisible = !ChatWindow.IsGroupInfoVisible);
        ChatWindow.CloseGroupInfoCommand = new RelayCommand(() => ChatWindow.IsGroupInfoVisible = false);

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
        _realtimeClient.RemovedFromChat += HandleRemovedFromChatAsync;
        _realtimeClient.ReconnectedAndNeedsRefresh += HandleReconnectedAsync;
        _realtimeClient.ConnectionStateChanged += state => ConnectionState = state;

        changeDetector.IdentityKeyChanged += HandleIdentityKeyChangedAsync;
    }

    private void ShowDebugError(string context, Exception ex)
    {
        DebugErrorText =
            $"Context : {context}\n" +
            $"Type    : {ex.GetType().FullName}\n" +
            $"Message : {ex.Message}\n\n" +
            $"--- Inner Exception ---\n" +
            (ex.InnerException is not null
                ? $"{ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n\n"
                : "(none)\n\n") +
            $"--- Stack Trace ---\n{ex.StackTrace}";
        IsShowingDebugError = true;
    }

    public void ShowFatalError(string source, Exception ex)
    {
        var text =
            $"An unexpected error occurred while the application was running.\n" +
            $"You can close this dialog and continue using BitCanary.\n\n" +
            $"Source  : {source}\n" +
            $"Type    : {ex.GetType().FullName}\n" +
            $"Message : {ex.Message}\n\n" +
            $"--- Inner Exception ---\n" +
            (ex.InnerException is not null
                ? $"{ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n\n"
                : "(none)\n\n") +
            $"--- Stack Trace ---\n{ex.StackTrace}";

        if (Dispatcher.UIThread.CheckAccess())
        {
            DebugErrorText = text;
            IsShowingDebugError = true;
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                DebugErrorText = text;
                IsShowingDebugError = true;
            });
        }
    }

    private void NavigateToSearchResult(SearchResultItemViewModel result)
    {
        ChatList.IsSearchMode = false;
        ChatList.Search?.Reset();

        var chatItem = ChatList.Chats.FirstOrDefault(c => c.Id == result.ChatId);
        if (chatItem is not null)
        {
            ChatList.SelectedChat = chatItem;
        }
    }

    private async Task HandleUserSelectedAsync(UserProfileDto selectedUser)
    {
        var existing = ChatList.Chats.FirstOrDefault(c =>
            c.Type == Messenger.Shared.Contracts.ChatType.Direct && c.PeerUserId == selectedUser.Id);
        if (existing is not null)
        {
            CloseSearchModes();
            ChatList.SelectedChat = existing;
            return;
        }

        if (existing is null)
        {
            var allChats = await _apiClient.GetChatsAsync();
            var existingChat = allChats.FirstOrDefault(c =>
                c.Type == Messenger.Shared.Contracts.ChatType.Direct &&
                c.Members?.Any(m => m.UserId == selectedUser.Id) == true);
            
            if (existingChat is not null)
            {
                CloseSearchModes();
                NavigateToChatAsync(existingChat.Id);
                return;
            }
        }

        if (ChatList.UserSearch is not null)
            ChatList.UserSearch.IsBusy = true;
        try
        {
            var request = new Messenger.Shared.Contracts.Dtos.CreateChatRequest(
                Title: selectedUser.DisplayName,
                Type: Messenger.Shared.Contracts.ChatType.Direct,
                Description: null,
                MemberIds: new[] { _sessionService.CurrentUserId, selectedUser.Id });

            var newChat = await _apiClient.CreateChatAsync(request);

            await RefreshRemoteDataAsync();
            CloseSearchModes();
            NavigateToChatAsync(newChat.Id);
        }
        catch
        {
            if (ChatList.UserSearch is not null)
            {
                ChatList.UserSearch.HasError = true;
                ChatList.UserSearch.ErrorMessage = "could not open chat -- try again";
            }
        }
        finally
        {
            if (ChatList.UserSearch is not null)
                ChatList.UserSearch.IsBusy = false;
        }
    }

    private void CloseSearchModes()
    {
        ChatList.IsSearchMode = false;
        ChatList.Search?.Reset();
        ChatList.IsUserSearchMode = false;
        ChatList.UserSearch?.Reset();
    }

    private async Task HandleGroupCreatedAsync(Messenger.Shared.Contracts.Dtos.CreateChatRequest request)
    {
        ChatList.IsGroupCreationMode = false;
        ChatList.GroupCreation?.Reset();
        var newChat = await _apiClient.CreateChatAsync(request);
        await RefreshRemoteDataAsync();
        NavigateToChatAsync(newChat.Id);
    }

    public void NavigateToChatAsync(Guid chatId)
    {
        if (ChatList.Chats.Count == 0)
        {
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
        IsShowingSettings = false;

        var userId = _sessionService.CurrentUserId;
        _sessionService.ClearSession();

        ChatList.SelectedChat = null;          // must be nulled explicitly — Chats.Clear() does not reset this
        ChatList.Chats.Clear();
        ChatList.IsSearchMode = false;
        ChatList.IsUserSearchMode = false;
        ChatList.IsGroupCreationMode = false;
        ChatWindow.Messages.Clear();
        ChatWindow.IsGroupInfoVisible = false;
        _messageCache.Clear();
        _chatSummaryCache.Clear();
        _deletedChatIds.Clear();
        _clearedChats.Clear();

        _ = _localCacheService.SaveAsync(ChatsKey(userId), Array.Empty<ChatSummaryDto>());
        IsLoggedIn = false;
        CurrentUsername = string.Empty;
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
            CurrentUsername = string.IsNullOrEmpty(_sessionService.UserName)
                ? string.Empty
                : $"[ @{_sessionService.UserName} ]";
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
                CurrentUsername = string.IsNullOrEmpty(_sessionService.UserName)
                    ? string.Empty
                    : $"[ @{_sessionService.UserName} ]";
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

        if (_pendingNavigationChatId.HasValue)
        {
            var pending = _pendingNavigationChatId.Value;
            _pendingNavigationChatId = null;
            NavigateToChatAsync(pending);
        }
    }

    private async Task ConnectAndLoadAsync()
    {
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
            ConnectionState = Messenger.Shared.Contracts.ConnectionState.Online;
            Settings.ConnectionStatus = $"Connected · {_sessionService.ApiBaseUrl}";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            Logout();
            StatusMessage = "Your session has expired. Please log in again.";
            return;
        }
        catch (Exception ex)
        {
            ConnectionState = Messenger.Shared.Contracts.ConnectionState.Offline;
            IsConnected = false;
            Settings.ConnectionStatus = "Offline — showing cached data";
            StatusMessage = string.Empty;
            System.Diagnostics.Debug.WriteLine($"[ConnectAndLoadAsync] SignalR fallback: {ex.GetType().Name}: {ex.Message}");
        }

        await RefreshRemoteDataAsync();
    }

    private async Task HandleOtpkSupplyLowAsync()
    {
        await _keyPublicationService.ReplenishOtpksAsync();
    }

    private async void HandleReconnectedAsync()
    {
        await RefreshRemoteDataAsync();
    }

    private async Task HandleIdentityKeyChangedAsync(string sessionId, byte[] newIkPublic)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _blockedSessions.Add(sessionId);

            var currentSessionId = GetCurrentSessionId();
            if (currentSessionId == sessionId)
            {
                MessageInput.IsBlockedForKeyVerification = true;
                MessageInput.NotifyBlockStateChanged();
            }

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

        if (GetCurrentSessionId() == sessionId)
        {
            MessageInput.IsBlockedForKeyVerification = false;
            MessageInput.NotifyBlockStateChanged();
        }

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
            await _localCacheService.SaveAsync(ChatsKey(), chats);

            var settings = await _apiClient.GetSettingsAsync();
            ApplySettings(settings);
            await _localCacheService.SaveAsync(SettingsKey(), settings);

            StatusMessage = $"Synced {chats.Count} chats.";

            _ = Task.Run(IndexAllChatsForSearchAsync);
        }
        finally
        {
            IsBusy = false;
        }
    }

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
                    catch {  }
                }
            }
            catch {  }
        }
    }

    private async Task LoadSelectedChatAsync()
    {
        var selectedChat = ChatList.SelectedChat;
        ChatWindow.Messages.Clear();
        ChatWindow.IsUnverified = false;
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
        ChatWindow.IsGroupChat = selectedChat.IsGroupChat;
        ChatWindow.GroupMemberCount = selectedChat.MemberCount;

        if (selectedChat.IsGroupChat)
        {
            ChatWindow.Subtitle = selectedChat.MemberCount > 0
                ? $"{selectedChat.MemberCount} member{(selectedChat.MemberCount == 1 ? "" : "s")}"
                : "group";
        }
        else
        {
            ChatWindow.Subtitle = selectedChat.Type.ToString();
            ChatWindow.IsGroupInfoVisible = false;
        }

        StatusMessage = $"Loading {selectedChat.Title}...";

        selectedChat.UnreadCount = 0;
        await _localMessageRepository.ResetUnreadCountAsync(selectedChat.Id);

        if (selectedChat.Type == ChatType.Direct)
        {
            var sessionId = GetCurrentSessionId();
            if (!string.IsNullOrEmpty(sessionId))
            {
                var verified = await TryAutoVerifyDirectChatAsync(selectedChat, sessionId);
                ChatWindow.IsSessionVerified = verified;

                MessageInput.IsBlockedForKeyVerification = _blockedSessions.Contains(sessionId);
            }
            else
            {
                ChatWindow.IsSessionVerified = false;
                MessageInput.IsBlockedForKeyVerification = false;
            }
        }
        else
        {
            ChatWindow.IsSessionVerified = false;
            MessageInput.IsBlockedForKeyVerification = false;
        }

        MessageInput.NotifyBlockStateChanged();

        var cacheKey = GetMessageCacheKey(selectedChat.Id);
        var cachedMessages = await _localCacheService.LoadAsync<IReadOnlyCollection<MessageDto>>(cacheKey);
        if (cachedMessages is not null)
        {
            var filtered = _clearedChats.TryGetValue(selectedChat.Id, out var clearedAt)
                ? cachedMessages.Where(m => m.CreatedAtUtc > clearedAt).ToList()
                : cachedMessages;
            await ReplaceMessagesAsync(selectedChat.Id, filtered);
        }

        if (_sessionService.IsAuthenticated)
        {
            try
            {
                var messages = await _apiClient.GetMessagesAsync(selectedChat.Id);
                var filteredMessages = _clearedChats.TryGetValue(selectedChat.Id, out var clearedAt)
                    ? messages.Where(m => m.CreatedAtUtc > clearedAt).ToList()
                    : (IReadOnlyCollection<MessageDto>)messages;
                await ReplaceMessagesAsync(selectedChat.Id, filteredMessages);
                await _localCacheService.SaveAsync(cacheKey, filteredMessages);
                await _realtimeClient.JoinChatAsync(selectedChat.Id);
                await _realtimeClient.SendReadReceiptAsync(selectedChat.Id);

                if (selectedChat.IsGroupChat && ChatWindow.GroupInfo is not null)
                {
                    _chatSummaryCache.TryGetValue(selectedChat.Id, out var summary);
                    if (summary is not null)
                        await ChatWindow.GroupInfo.LoadAsync(summary, _sessionService.CurrentUserId);
                }

                StatusMessage = $"{selectedChat.Title} ready.";
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException
                                           or SocketException
                                           or TaskCanceledException
                                           or OperationCanceledException)
            {
                StatusMessage = $"{selectedChat.Title} (offline — showing cached messages)";
            }
            catch (Microsoft.AspNetCore.SignalR.HubException ex)
            {
                StatusMessage = $"{selectedChat.Title} ready (sync issue: {ex.Message})";
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

        var sessionId = GetCurrentSessionId();
        bool canRegenerate = selectedChat.Type == ChatType.Direct || IsChatOwner(selectedChat.Id);

        if (selectedChat.Type == ChatType.Direct && !string.IsNullOrEmpty(sessionId))
        {
            var verified = await TryAutoVerifyDirectChatAsync(selectedChat, sessionId);
            ChatWindow.IsSessionVerified = verified;
        }

        await SafetyNumber.LoadAsync(
            _sessionService.CurrentUserId,
            selectedChat.Id,
            canRegenerate,
            sessionId,
            selectedChat.Title);

        IsShowingSafetyNumber = true;
    }

    private async Task<bool> TryAutoVerifyDirectChatAsync(ChatListItemViewModel chat, string sessionId)
    {
        if (chat.Type != ChatType.Direct) return false;
        if (chat.PeerUserId == Guid.Empty) return false;
        if (string.IsNullOrEmpty(sessionId)) return false;

        try
        {
            var verState = await _sessionRepository.LoadVerificationStateAsync(sessionId);
            var bundle = await _apiClient.GetKeyBundleAsync(chat.PeerUserId);
            if (bundle is null || bundle.IkPublic is null || bundle.IkPublic.Length == 0)
            {
                return verState.Verified;
            }

            bool ikMatchesStored =
                verState.RemoteIkPublic is not null &&
                verState.RemoteIkPublic.Length == bundle.IkPublic.Length &&
                CryptographicOperations.FixedTimeEquals(verState.RemoteIkPublic, bundle.IkPublic);

            if (verState.RemoteIkPublic is null)
            {
                await _sessionRepository.SaveVerificationStateAsync(
                    sessionId,
                    verified: true,
                    lastVerifiedAt: DateTimeOffset.UtcNow,
                    remoteIkPublic: bundle.IkPublic);
                return true;
            }

            if (ikMatchesStored)
            {
                if (!verState.Verified)
                {
                    await _sessionRepository.SaveVerificationStateAsync(
                        sessionId,
                        verified: true,
                        lastVerifiedAt: DateTimeOffset.UtcNow,
                        remoteIkPublic: bundle.IkPublic);
                }
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool IsChatOwner(Guid chatId)
    {
        if (!_chatSummaryCache.TryGetValue(chatId, out var chat))
            return false;

        var self = chat.Members?.FirstOrDefault(m => m.UserId == _sessionService.CurrentUserId);
        return self?.Role == ChatRole.Owner;
    }

    private string GetCurrentSessionId()
    {
        var selected = ChatList.SelectedChat;
        if (selected is null) return string.Empty;
        if (selected.Type != ChatType.Direct) return string.Empty;
        return $"{_sessionService.CurrentUserId}:{selected.PeerUserId}";
    }

    private async Task SendMessageAsync(ChatSendPayload payload)
    {
        var selectedChat = ChatList.SelectedChat;
        if (selectedChat is null)
        {
            return;
        }

        if (payload.ImageAttachment is null && string.IsNullOrWhiteSpace(payload.Text))
        {
            return;
        }

        if (!_sessionService.IsAuthenticated)
        {
            var demoBody = payload.ImageAttachment is not null
                ? (string.IsNullOrWhiteSpace(payload.Text) ? "📷" : $"📷 {payload.Text}")
                : payload.Text;
            if (string.IsNullOrWhiteSpace(demoBody)) return;

            var demoMessage = new MessageDto(
                Guid.NewGuid(),
                selectedChat.Id,
                Guid.Empty,
                _sessionService.UserName,
                MessageKind.Text,
                demoBody,
                "plaintext-demo",
                "demo-envelope",
                null,
                null,
                null,
                DateTimeOffset.UtcNow);

            await AppendMessageAsync(demoMessage);
            return;
        }

        Guid? mediaId = null;
        var sentMessageKind = MessageKind.Text;
        if (payload.ImageAttachment is not null)
        {
            await using var uploadStream = await payload.ImageAttachment.OpenReadAsync();
            var upload = await _apiClient.UploadMediaAsync(
                payload.ImageAttachment.Name,
                ImageSendFormats.GuessContentType(payload.ImageAttachment.Name),
                uploadStream);
            mediaId = upload.MediaId;
            sentMessageKind = MessageKind.Image;
        }

        var plaintextBody = sentMessageKind == MessageKind.Image
            ? (string.IsNullOrWhiteSpace(payload.Text) ? " " : payload.Text.Trim())
            : payload.Text;

        var clientMessageId = Guid.NewGuid();

        Bitmap? optimisticBitmap = null;
        if (payload.ImageAttachment is not null)
        {
            try
            {
                await using var previewStream = await payload.ImageAttachment.OpenReadAsync();
                optimisticBitmap = await Task.Run(() => new Bitmap(previewStream));
            }
            catch
            {
                optimisticBitmap?.Dispose();
                optimisticBitmap = null;
            }
        }

        var displayCaption = sentMessageKind == MessageKind.Image
            ? (string.IsNullOrWhiteSpace(payload.Text) ? string.Empty : payload.Text.Trim())
            : plaintextBody;

        var optimisticVm = new MessageItemViewModel
        {
            Id = Guid.Empty,  // No server ID yet
            ClientMessageId = clientMessageId,
            SenderDisplayName = $"<@{_sessionService.UserName}>",
            DisplayText = displayCaption,
            MessageKind = sentMessageKind,
            MediaId = mediaId,
            InlineImage = optimisticBitmap,
            Timestamp = DateTimeOffset.UtcNow.LocalDateTime.ToShortTimeString(),
            IsOutgoing = true,
            IsEncrypted = true,
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
            var recipientUserId = selectedChat.PeerUserId;
            var (encrypted, isPlaintext) = await BuildEncryptedDraftAsync(plaintextBody, recipientUserId);
            var request = new SendMessageRequest(
                selectedChat.Id,
                clientMessageId,
                sentMessageKind,
                encrypted.EncryptedPayload,
                encrypted.EncryptionAlgorithm,
                encrypted.KeyEnvelope,
                mediaId,
                null,
                encrypted.MetadataJson,
                isPlaintext ? ProtocolVersion.Plaintext : ProtocolVersion.NoiseXX);

            var message = await _apiClient.SendMessageAsync(request);
            _sentPlaintextByMessageId[message.Id] = plaintextBody;

            _outgoingVmByClientId.Remove(clientMessageId);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ChatList.SelectedChat?.Id == selectedChat.Id)
                    ChatWindow.Messages.Remove(optimisticVm);
            });

            await AppendMessageAsync(message);
            await _localMessageRepository.SaveMessageAsync(message, (int)request.ProtocolVersion);
            await _localMessageRepository.UpdatePlaintextBodyAsync(message.Id, plaintextBody);
            await PersistMessagesAsync(selectedChat.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       or OperationCanceledException
                                       or TaskCanceledException
                                       or System.Security.Cryptography.CryptographicException)
        {
            _outgoingVmByClientId.Remove(clientMessageId);
            System.Diagnostics.Debug.WriteLine($"[SendMessageAsync] Failed: {ex.GetType().Name}: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                optimisticVm.Status = MessageStatus.Failed;
                StatusMessage = "Message failed to send. Check your connection and try again.";
                ShowDebugError("SendMessageAsync", ex);
            });
        }
    }

    private async Task RetryMessageAsync(Guid clientMessageId)
    {
        var vm = ChatWindow.Messages
            .OfType<MessageItemViewModel>()
            .FirstOrDefault(m => m.ClientMessageId == clientMessageId);
        if (vm is null) return;

        if (vm.MessageKind != MessageKind.Text)
            return;

        var selectedChat = ChatList.SelectedChat;
        if (selectedChat is null) return;

        var plaintext = vm.DisplayText;
        var recipientUserId = selectedChat.PeerUserId;

        try
        {
            var (encrypted, isPlaintext) = await BuildEncryptedDraftAsync(plaintext, recipientUserId);

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
                isPlaintext ? ProtocolVersion.Plaintext : ProtocolVersion.NoiseXX);

            var message = await _apiClient.SendMessageAsync(request);
            _sentPlaintextByMessageId[message.Id] = plaintext;

            await Dispatcher.UIThread.InvokeAsync(() => ChatWindow.Messages.Remove(vm));

            await AppendMessageAsync(message);
            await _localMessageRepository.SaveMessageAsync(message, (int)request.ProtocolVersion);
            await _localMessageRepository.UpdatePlaintextBodyAsync(message.Id, plaintext);
            await PersistMessagesAsync(selectedChat.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       or OperationCanceledException
                                       or TaskCanceledException
                                       or System.Security.Cryptography.CryptographicException)
        {
            System.Diagnostics.Debug.WriteLine($"[RetryMessageAsync] Failed: {ex.GetType().Name}: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                vm.Status = MessageStatus.Failed;
                StatusMessage = "Message failed to send. Check your connection and try again.";
                ShowDebugError("RetryMessageAsync", ex);
            });
        }
    }

    private async Task<(EncryptedMessageDraft draft, bool isPlaintext)> BuildEncryptedDraftAsync(
        string plaintext, Guid recipientUserId, CancellationToken cancellationToken = default)
    {
        var sessionId = $"{_sessionService.CurrentUserId}:{recipientUserId}";

        var existingSession = await _sessionManager.GetSessionAsync(sessionId, cancellationToken);
        if (existingSession is not null)
        {
            var draft = await _encryptionService.EncryptTextAsync(plaintext, recipientUserId, cancellationToken);
            return (draft, isPlaintext: false);
        }

        var bundle = await _apiClient.GetKeyBundleAsync(recipientUserId, cancellationToken);
        if (bundle is null)
        {
            var plaintextDraft = new EncryptedMessageDraft(
                MessageKind.Text,
                plaintext,
                "plaintext",
                string.Empty,
                null);
            return (plaintextDraft, isPlaintext: true);
        }

        var encryptedDraft = await _encryptionService.EncryptTextAsync(plaintext, recipientUserId, cancellationToken);
        return (encryptedDraft, isPlaintext: false);
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
        var terminalScheme = Settings.SelectedTerminalScheme?.Value ?? TerminalColorScheme.MatrixGreen;

        var settings = new UpdateSettingsRequest(
            themePreference,
            Settings.SendByEnter,
            false,
            Settings.EnableCustomEmoji,
            Settings.ShowNotifications,
            Settings.ShowSenderName,
            terminalScheme);

        if (_sessionService.IsAuthenticated)
        {
            await _apiClient.UpdateSettingsAsync(settings);
        }

        await _localCacheService.SaveAsync(SettingsKey(), new UserSettingsDto(
            themePreference,
            Settings.SendByEnter,
            false,
            Settings.EnableCustomEmoji,
            Settings.ShowNotifications,
            Settings.ShowSenderName,
            terminalScheme));
    }

    private async Task HandleIncomingMessageAsync(MessageDto message)
    {
        ChatListItemViewModel? chatItem = null;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await UpdateChatPreviewAsync(message);
            await AppendMessageAsync(message);
            chatItem = ChatList.Chats.FirstOrDefault(c => c.Id == message.ChatId);
        });

        _notificationService.ShowIfMinimized(message.ChatId, chatItem?.Title);

        if (ChatList.RefreshCommand.CanExecute(null))
        {
            await ChatList.RefreshCommand.ExecuteAsync(null);
        }

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
        UpdateCachedMessageStatus(messageId, MessageStatus.Delivered);

        await _localMessageRepository.UpdateMessageStatusAsync(messageId, MessageStatus.Delivered);
    }

    private async Task HandleMessagesReadAsync(Guid chatId, Guid readByUserId)
    {
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
        UpdateCachedOutgoingStatuses(chatId, MessageStatus.Read);

        await _localMessageRepository.MarkMessagesReadAsync(chatId, _sessionService.CurrentUserId);
    }

    private async Task HandleRemovedFromChatAsync(Guid chatId)
    {
        try
        {
            await _realtimeClient.LeaveChatAsync(chatId);
        }
        catch
        {
        }

        _messageCache.Remove(chatId);
        _chatSummaryCache.Remove(chatId);
        _clearedChats.Remove(chatId);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ChatList.SelectedChat?.Id == chatId)
            {
                ChatWindow.IsGroupInfoVisible = false;
                ChatList.SelectedChat = null;
            }

            var item = ChatList.Chats.FirstOrDefault(c => c.Id == chatId);
            if (item is not null)
            {
                ChatList.Chats.Remove(item);
            }
        });
    }

    private void UpdateCachedMessageStatus(Guid messageId, MessageStatus status)
    {
        foreach (var (chatId, messages) in _messageCache)
        {
            var index = messages.FindIndex(m => m.Id == messageId);
            if (index < 0) continue;
            var current = messages[index];
            if (current.Status >= status) return;
            messages[index] = current with { Status = status };
            _ = PersistMessagesAsync(chatId);
            return;
        }
    }

    private void UpdateCachedOutgoingStatuses(Guid chatId, MessageStatus status)
    {
        if (!_messageCache.TryGetValue(chatId, out var messages)) return;
        var changed = false;
        for (var i = 0; i < messages.Count; i++)
        {
            var current = messages[i];
            if (current.SenderId != _sessionService.CurrentUserId || current.Status >= status) continue;
            messages[i] = current with { Status = status };
            changed = true;
        }
        if (changed)
            _ = PersistMessagesAsync(chatId);
    }

    private async Task LoadCachedSettingsAsync()
    {
        var settings = await _localCacheService.LoadAsync<UserSettingsDto>(SettingsKey());
        if (settings is not null)
        {
            ApplySettings(settings);
        }
    }

    private async Task LoadCachedChatsAsync()
    {
        await LoadLocalDeletionStateAsync();

        var chats = await _localCacheService.LoadAsync<IReadOnlyCollection<ChatSummaryDto>>(ChatsKey());
        if (chats is not null)
        {
            await ApplyChatSummariesAsync(chats);
        }
    }

    private async Task LoadLocalDeletionStateAsync()
    {
        var deletedIds = await _localCacheService.LoadAsync<List<string>>(DeletedChatsKey());
        if (deletedIds is not null)
        {
            foreach (var id in deletedIds)
                if (Guid.TryParse(id, out var g)) _deletedChatIds.Add(g);
        }

        var clearedMap = await _localCacheService.LoadAsync<Dictionary<string, DateTimeOffset>>(ClearedChatsKey());
        if (clearedMap is not null)
        {
            foreach (var (k, v) in clearedMap)
                if (Guid.TryParse(k, out var g)) _clearedChats[g] = v;
        }
    }

    private void ApplySettings(UserSettingsDto settings)
    {
        _themeService.Apply(ThemePreference.Terminal);
        Settings.SelectedThemeOption = Settings.ThemeOptions.FirstOrDefault(x => x.Value == ThemePreference.Terminal)
                                    ?? Settings.ThemeOptions[0];

        Settings.SelectTerminalSchemeFromSettings(settings.TerminalColorScheme);

        Settings.SendByEnter = settings.SendByEnter;
        Settings.EnableCustomEmoji = settings.EnableCustomEmoji;
        Settings.ShowNotifications = settings.ShowNotifications;
        Settings.ShowSenderName = settings.ShowSenderName;
    }

    private async Task ApplyChatSummariesAsync(IReadOnlyCollection<ChatSummaryDto> chats)
    {
        ChatList.Chats.Clear();
        _chatSummaryCache.Clear();

        foreach (var chat in chats)
        {
            if (_deletedChatIds.Contains(chat.Id)) continue;

            await _localMessageRepository.UpsertChatAsync(chat);

            var peer = chat.Members?.FirstOrDefault(m => m.UserId != _sessionService.CurrentUserId);

            string decryptedSubtitle;
            if (chat.LastMessage is null)
            {
                decryptedSubtitle = string.Empty;
            }
            else
            {
                try
                {
                    decryptedSubtitle = await _encryptionService.DecryptAsync(chat.LastMessage);
                }
                catch
                {
                    decryptedSubtitle = "[Unable to decrypt]";
                }
            }

            var isGroup = chat.Type == Messenger.Shared.Contracts.ChatType.Group;
            string subtitle;
            if (isGroup)
            {
                var count = chat.Members?.Count ?? 0;
                subtitle = count > 0
                    ? $"{count} member{(count == 1 ? "" : "s")}"
                    : "group";
            }
            else
            {
                subtitle = decryptedSubtitle;
            }

            var lastActivity = chat.LastMessage is not null
                ? TimestampFormatter.FormatTimestamp(chat.LastMessage.CreatedAtUtc)
                : string.Empty;

            ChatList.Chats.Add(new ChatListItemViewModel(DeleteChatAsync, ClearChatMessagesAsync)
            {
                Id = chat.Id,
                Title = chat.Type == Messenger.Shared.Contracts.ChatType.Direct
                    ? (string.IsNullOrEmpty(peer?.DisplayName) ? chat.Title : peer.DisplayName)
                    : chat.Title,
                Type = chat.Type,
                PeerUserId = peer?.UserId ?? Guid.Empty,
                MemberCount = chat.Members?.Count ?? 0,
                Subtitle = subtitle,
                LastActivity = lastActivity,
                UnreadCount = chat.UnreadCount
            });
            _chatSummaryCache[chat.Id] = chat;
        }

    }

    private async Task ReplaceMessagesAsync(Guid chatId, IReadOnlyCollection<MessageDto> messages)
    {
        _messageCache[chatId] = messages.ToList();
        ChatWindow.Messages.Clear();

        foreach (var message in messages)
        {
            await AppendMessageToWindowAsync(message);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ChatWindow.IsUnverified = messages.Any(m => m.ProtocolVersion == ProtocolVersion.Plaintext);
        });
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
        if (message.SenderId == _sessionService.CurrentUserId &&
            _sentPlaintextByMessageId.TryGetValue(message.Id, out var ownPlaintext))
        {
            displayText = ownPlaintext;
        }
        else
        {
            try
            {
                displayText = await _encryptionService.DecryptAsync(message);
            }
            catch
            {
                displayText = "[Unable to decrypt]";
            }
        }

        if (message.Kind == MessageKind.Text && ImageSendFormats.IsLegacyImageFileLine(displayText))
        {
            displayText = "📷";
        }

        if (message.Kind == MessageKind.Image)
        {
            var t = displayText.Trim('\u200b', ' ', '\t', '\r', '\n');
            if (t.Length == 0)
                displayText = string.Empty;
            else
                displayText = t;
        }

        var isGroupChatMessage =
            (_chatSummaryCache.TryGetValue(message.ChatId, out var chatSummary) && chatSummary.Type != ChatType.Direct)
            || ChatList.Chats.FirstOrDefault(c => c.Id == message.ChatId)?.IsGroupChat == true;

        var vm = new MessageItemViewModel
        {
            Id = message.Id,
            ClientMessageId = Guid.NewGuid(),
            SenderDisplayName = $"<@{message.SenderDisplayName}>",
            DisplayText = displayText,
            MessageKind = message.Kind,
            MediaId = message.MediaId,
            Timestamp = message.CreatedAtUtc.LocalDateTime.ToShortTimeString(),
            IsOutgoing = message.SenderId == _sessionService.CurrentUserId,
            IsGroupChat = isGroupChatMessage,
            IsEncrypted = message.ProtocolVersion == ProtocolVersion.SignalProtocol ||
                          message.ProtocolVersion == ProtocolVersion.NoiseXX ||
                          message.EncryptionAlgorithm.Equals("signal-protocol-v1", StringComparison.OrdinalIgnoreCase) ||
                          message.EncryptionAlgorithm.Equals("noise-xx-dr-v1", StringComparison.OrdinalIgnoreCase),
            Status = message.Status
        };
        vm.SetRetryDelegate(RetryMessageAsync);
        ChatWindow.Messages.Add(vm);

        if (message.Kind == MessageKind.Image && message.MediaId is { } mid)
        {
            _ = LoadMessageInlineImageAsync(vm, mid);
        }

        if (message.ProtocolVersion == ProtocolVersion.Plaintext)
        {
            await Dispatcher.UIThread.InvokeAsync(() => ChatWindow.IsUnverified = true);
        }
    }

    private async Task LoadMessageInlineImageAsync(MessageItemViewModel vm, Guid mediaId)
    {
        try
        {
            var bytes = await _apiClient.DownloadMediaAsync(mediaId);
            await using var ms = new MemoryStream(bytes);
            var bmp = await Task.Run(() => new Bitmap(ms));
            await Dispatcher.UIThread.InvokeAsync(() => vm.InlineImage = bmp);
        }
        catch
        {
        }
    }

    private async Task UpdateChatPreviewAsync(MessageDto message)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var chatItem = ChatList.Chats.FirstOrDefault(x => x.Id == message.ChatId);

            if (chatItem is null)
            {
                var senderUserId = message.SenderId;
                var isDirect = senderUserId != _sessionService.CurrentUserId;
                var displayName = isDirect ? message.SenderDisplayName : $"Chat {message.ChatId.ToString()[..8]}";

                chatItem = new ChatListItemViewModel(DeleteChatAsync, ClearChatMessagesAsync)
                {
                    Id = message.ChatId,
                    Title = displayName,
                    Type = isDirect ? ChatType.Direct : ChatType.Group,
                    PeerUserId = isDirect ? senderUserId : Guid.Empty,
                    MemberCount = 0,
                    Subtitle = "[New message]",
                    LastActivity = TimestampFormatter.FormatTimestamp(message.CreatedAtUtc),
                    UnreadCount = 1
                };

                ChatList.Chats.Insert(0, chatItem);
            }

            if (chatItem is null)
                return;

            if (message.SenderId == _sessionService.CurrentUserId &&
                _sentPlaintextByMessageId.TryGetValue(message.Id, out var ownSubtitle))
            {
                if (message.Kind == MessageKind.Image)
                {
                    var t = ownSubtitle.Trim('\u200b', ' ', '\t', '\r', '\n');
                    chatItem.Subtitle = string.IsNullOrEmpty(t) ? "📷" : t;
                }
                else
                {
                    chatItem.Subtitle = ownSubtitle;
                }
            }
            else
            {
                try
                {
                    var decrypted = await _encryptionService.DecryptAsync(message);
                    if (message.Kind == MessageKind.Image && string.IsNullOrWhiteSpace(decrypted.Trim('\u200b', ' ', '\t', '\r', '\n')))
                        chatItem.Subtitle = "📷";
                    else
                        chatItem.Subtitle = decrypted;
                }
                catch
                {
                    chatItem.Subtitle = "[Unable to decrypt]";
                }
            }

            chatItem.LastActivity = TimestampFormatter.FormatTimestamp(message.CreatedAtUtc);
        });
    }

    private Task PersistMessagesAsync(Guid chatId)
    {
        if (!_messageCache.TryGetValue(chatId, out var messages))
        {
            return Task.CompletedTask;
        }

        return _localCacheService.SaveAsync(GetMessageCacheKey(chatId), messages);
    }

    private string GetMessageCacheKey(Guid chatId) =>
        $"messages-{_sessionService.CurrentUserId:N}-{chatId:N}";

    private string ChatsKey() => ChatsKey(_sessionService.CurrentUserId);
    private static string ChatsKey(Guid userId) => $"chats-{userId:N}";

    private string SettingsKey() => $"settings-{_sessionService.CurrentUserId:N}";
    private string DeletedChatsKey() => $"deleted-chats-{_sessionService.CurrentUserId:N}";
    private string ClearedChatsKey() => $"cleared-chats-{_sessionService.CurrentUserId:N}";

    private Task DeleteChatAsync(Guid chatId)
    {
        var chatTitle = ChatList.Chats.FirstOrDefault(c => c.Id == chatId)?.Title ?? "this chat";
        PendingDeleteChatTitle = chatTitle;
        _pendingDeleteChatId = chatId;
        IsConfirmingDeleteChat = true;
        return Task.CompletedTask;
    }

    private async Task ExecuteDeleteChatAsync(Guid chatId)
    {
        try
        {
            if (_sessionService.IsAuthenticated)
            {
                await _apiClient.DeleteChatAsync(chatId);
            }
            await _localMessageRepository.DeleteChatAsync(chatId);
        }
        catch (Exception ex)
        {
            StatusMessage = "failed to delete chat";
            ShowDebugError("ExecuteDeleteChatAsync", ex);
            return;
        }

        _deletedChatIds.Add(chatId);
        await _localCacheService.SaveAsync(DeletedChatsKey(), _deletedChatIds.Select(g => g.ToString()).ToList());

        _messageCache.Remove(chatId);
        _clearedChats.Remove(chatId);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var item = ChatList.Chats.FirstOrDefault(c => c.Id == chatId);
            if (item is null) return;

            if (ChatList.SelectedChat?.Id == chatId)
            {
                ChatList.SelectedChat = null;
            }

            ChatList.Chats.Remove(item);
        });
    }

    private async Task ClearChatMessagesAsync(Guid chatId)
    {
        try
        {
            await _localMessageRepository.ClearMessagesAsync(chatId);
        }
        catch (Exception ex)
        {
            StatusMessage = "failed to clear messages";
            ShowDebugError("ClearChatMessagesAsync", ex);
            return;
        }

        var clearedAt = DateTimeOffset.UtcNow;
        _clearedChats[chatId] = clearedAt;
        await _localCacheService.SaveAsync(ClearedChatsKey(),
            _clearedChats.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));

        _messageCache.Remove(chatId);
        await _localCacheService.SaveAsync(GetMessageCacheKey(chatId), Array.Empty<MessageDto>());

        if (ChatList.SelectedChat?.Id == chatId)
        {
            await Dispatcher.UIThread.InvokeAsync(() => ChatWindow.Messages.Clear());
        }
    }
}
