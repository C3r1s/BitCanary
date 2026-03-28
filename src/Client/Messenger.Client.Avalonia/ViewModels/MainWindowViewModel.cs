using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Services;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Messenger.Shared.Contracts.Realtime;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IMessengerApiClient _apiClient;
    private readonly IRealtimeClient _realtimeClient;
    private readonly ILocalCacheService _localCacheService;
    private readonly IEncryptionService _encryptionService;
    private readonly IThemeService _themeService;
    private readonly IClientSessionService _sessionService;
    private readonly Dictionary<Guid, List<MessageDto>> _messageCache = new();

    [ObservableProperty]
    private string _statusMessage = "Loading local cache...";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConnected;

    /// <summary>True once the user has authenticated and the main UI should be shown.</summary>
    [ObservableProperty]
    private bool _isLoggedIn;

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
        IClientSessionService sessionService)
    {
        _apiClient = apiClient;
        _realtimeClient = realtimeClient;
        _localCacheService = localCacheService;
        _encryptionService = encryptionService;
        _themeService = themeService;
        _sessionService = sessionService;

        ChatList = new ChatListViewModel(RefreshRemoteDataAsync);
        ChatList.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName == nameof(ChatListViewModel.SelectedChat))
            {
                await LoadSelectedChatAsync();
            }
        };

        MessageInput = new MessageInputViewModel(SendMessageAsync, SendTypingAsync, () => ChatList.SelectedChat?.Id);
        Settings = new SettingsViewModel(ChangeThemeAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshRemoteDataAsync);
        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        LogoutCommand = new RelayCommand(Logout);
        ExitCommand = new RelayCommand(() => Environment.Exit(0));

        LoginVm = new LoginViewModel(apiClient, sessionService);
        LoginVm.LoginSucceeded += HandleLoginSucceededAsync;

        _realtimeClient.MessageReceived += HandleIncomingMessageAsync;
        _realtimeClient.TypingReceived += HandleTypingAsync;
        _realtimeClient.PresenceChanged += HandlePresenceChangedAsync;
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
    }

    /// <summary>Connect SignalR, sync chats, and update status. Called after login or on startup.</summary>
    private async Task ConnectAndLoadAsync()
    {
        await _realtimeClient.ConnectAsync();
        IsConnected = true;
        Settings.ConnectionStatus = $"Connected · {_sessionService.ApiBaseUrl}";
        await RefreshRemoteDataAsync();
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
            ApplyChatSummaries(chats);
            await _localCacheService.SaveAsync("chats", chats);

            var settings = await _apiClient.GetSettingsAsync();
            ApplySettings(settings);
            await _localCacheService.SaveAsync("settings", settings);

            StatusMessage = $"Synced {chats.Count} chats.";
        }
        finally
        {
            IsBusy = false;
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
            return;
        }

        ChatWindow.Title = selectedChat.Title;
        ChatWindow.Subtitle = selectedChat.Type.ToString();
        StatusMessage = $"Loading {selectedChat.Title}...";

        var cacheKey = GetMessageCacheKey(selectedChat.Id);
        var cachedMessages = await _localCacheService.LoadAsync<IReadOnlyCollection<MessageDto>>(cacheKey);
        if (cachedMessages is not null)
        {
            ReplaceMessages(selectedChat.Id, cachedMessages);
        }

        if (_sessionService.IsAuthenticated)
        {
            var messages = await _apiClient.GetMessagesAsync(selectedChat.Id);
            ReplaceMessages(selectedChat.Id, messages);
            await _localCacheService.SaveAsync(cacheKey, messages);
            await _realtimeClient.JoinChatAsync(selectedChat.Id);
        }

        StatusMessage = $"{selectedChat.Title} ready.";
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

            AppendMessage(demoMessage);
            return;
        }

        var encrypted = await _encryptionService.EncryptTextAsync(plaintext);
        var request = new SendMessageRequest(
            selectedChat.Id,
            Guid.NewGuid(),
            encrypted.Kind,
            encrypted.EncryptedPayload,
            encrypted.EncryptionAlgorithm,
            encrypted.KeyEnvelope,
            null,
            null,
            encrypted.MetadataJson);

        var message = await _apiClient.SendMessageAsync(request);
        AppendMessage(message);
        await PersistMessagesAsync(selectedChat.Id);
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
            Settings.EnableCustomEmoji);

        if (_sessionService.IsAuthenticated)
        {
            await _apiClient.UpdateSettingsAsync(settings);
        }

        await _localCacheService.SaveAsync("settings", new UserSettingsDto(
            themePreference,
            Settings.SendByEnter,
            Settings.UseCompactMode,
            Settings.EnableCustomEmoji));
    }

    private async Task HandleIncomingMessageAsync(MessageDto message)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateChatPreview(message);

            if (ChatList.SelectedChat?.Id == message.ChatId)
            {
                AppendMessage(message);
            }
        });

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
            ApplyChatSummaries(chats);
        }
    }

    private void ApplySettings(UserSettingsDto settings)
    {
        _themeService.Apply(settings.ThemePreference);
        Settings.SelectedThemeOption = Settings.ThemeOptions.First(x => x.Value == settings.ThemePreference);
        Settings.SendByEnter = settings.SendByEnter;
        Settings.UseCompactMode = settings.UseCompactMode;
        Settings.EnableCustomEmoji = settings.EnableCustomEmoji;
    }

    private void ApplyChatSummaries(IReadOnlyCollection<ChatSummaryDto> chats)
    {
        ChatList.Chats.Clear();

        foreach (var chat in chats)
        {
            ChatList.Chats.Add(new ChatListItemViewModel
            {
                Id = chat.Id,
                Title = chat.Title,
                Type = chat.Type,
                Subtitle = chat.LastMessage is null ? "No messages yet" : _encryptionService.TryDecrypt(chat.LastMessage),
                LastActivity = chat.LastMessage?.CreatedAtUtc.LocalDateTime.ToShortTimeString() ?? string.Empty,
                UnreadCount = chat.UnreadCount
            });
        }

        ChatList.SelectedChat ??= ChatList.Chats.FirstOrDefault();
    }

    private void ReplaceMessages(Guid chatId, IReadOnlyCollection<MessageDto> messages)
    {
        _messageCache[chatId] = messages.ToList();
        ChatWindow.Messages.Clear();

        foreach (var message in messages)
        {
            AppendMessageToWindow(message);
        }
    }

    private void AppendMessage(MessageDto message)
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
            AppendMessageToWindow(message);
        }
    }

    private void AppendMessageToWindow(MessageDto message)
    {
        if (ChatWindow.Messages.Any(x => x.Id == message.Id))
        {
            return;
        }

        ChatWindow.Messages.Add(new MessageItemViewModel
        {
            Id = message.Id,
            SenderDisplayName = message.SenderDisplayName,
            DisplayText = _encryptionService.TryDecrypt(message),
            Timestamp = message.CreatedAtUtc.LocalDateTime.ToShortTimeString(),
            IsOutgoing = message.SenderId == _sessionService.CurrentUserId
        });
    }

    private void UpdateChatPreview(MessageDto message)
    {
        var chatItem = ChatList.Chats.FirstOrDefault(x => x.Id == message.ChatId);
        if (chatItem is null)
        {
            return;
        }

        chatItem.Subtitle = _encryptionService.TryDecrypt(message);
        chatItem.LastActivity = message.CreatedAtUtc.LocalDateTime.ToShortTimeString();
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
