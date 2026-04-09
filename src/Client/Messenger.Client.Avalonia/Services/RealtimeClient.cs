using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Messenger.Shared.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Messenger.Client.Avalonia.Services;

public sealed class RealtimeClient : IRealtimeClient
{
    private readonly IClientSessionService _sessionService;
    private readonly ILogger<RealtimeClient> _logger;
    private readonly HubConnection _hubConnection;
    private Guid? _currentlySelectedChatId;

    public RealtimeClient(IClientSessionService sessionService, ILogger<RealtimeClient> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
        _hubConnection = CreateConnection(sessionService);
    }

    public event Func<MessageDto, Task>? MessageReceived;
    public event Func<TypingIndicatorDto, Task>? TypingReceived;
    public event Func<CallSignalDto, Task>? CallSignalReceived;
    public event Func<PresenceChangedDto, Task>? PresenceChanged;
    public event Func<Task>? OtpkSupplyLow;
    public event Func<Guid, Task>? MessageDelivered;
    public event Func<Guid, Guid, Task>? MessagesRead;
    public event Action<ConnectionState>? ConnectionStateChanged;
    public event Action? ReconnectedAndNeedsRefresh;

    public void SetCurrentlySelectedChatId(Guid? chatId) => _currentlySelectedChatId = chatId;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_sessionService.IsAuthenticated || _hubConnection.State is HubConnectionState.Connected or HubConnectionState.Connecting)
        {
            return;
        }

        await _hubConnection.StartAsync(cancellationToken);
    }

    public Task JoinChatAsync(Guid chatId, CancellationToken cancellationToken = default) =>
        _hubConnection.State == HubConnectionState.Connected
            ? _hubConnection.InvokeCoreAsync("JoinChat", new object?[] { chatId }, cancellationToken)
            : Task.CompletedTask;

    public Task SendTypingIndicatorAsync(Guid chatId, bool isTyping, CancellationToken cancellationToken = default) =>
        _hubConnection.State == HubConnectionState.Connected
            ? _hubConnection.InvokeCoreAsync("TypingIndicator", new object?[] { chatId, isTyping }, cancellationToken)
            : Task.CompletedTask;

    public Task SendReadReceiptAsync(Guid chatId, CancellationToken cancellationToken = default) =>
        _hubConnection.State == HubConnectionState.Connected
            ? _hubConnection.InvokeCoreAsync("ReadMessages", new object?[] { chatId }, cancellationToken)
            : Task.CompletedTask;

    public ValueTask DisposeAsync() => _hubConnection.DisposeAsync();

    private HubConnection CreateConnection(IClientSessionService session)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{session.ApiBaseUrl.TrimEnd('/')}/hubs/chat", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(session.AccessToken);
            })
            .WithAutomaticReconnect()
            .Build();

        connection.On<MessageDto>(RealtimeEventNames.MessageReceived, message => MessageReceived?.Invoke(message) ?? Task.CompletedTask);
        connection.On<TypingIndicatorDto>(RealtimeEventNames.TypingIndicator, typing => TypingReceived?.Invoke(typing) ?? Task.CompletedTask);
        connection.On<CallSignalDto>(RealtimeEventNames.CallSignalReceived, signal => CallSignalReceived?.Invoke(signal) ?? Task.CompletedTask);
        connection.On<PresenceChangedDto>(RealtimeEventNames.PresenceChanged, signal => PresenceChanged?.Invoke(signal) ?? Task.CompletedTask);
        connection.On(RealtimeEventNames.OtpkSupplyLow, () => OtpkSupplyLow?.Invoke() ?? Task.CompletedTask);
        connection.On<Guid>(RealtimeEventNames.MessageDelivered, messageId => MessageDelivered?.Invoke(messageId) ?? Task.CompletedTask);
        connection.On<Guid, Guid>(RealtimeEventNames.MessageRead, (chatId, readByUserId) => MessagesRead?.Invoke(chatId, readByUserId) ?? Task.CompletedTask);

        connection.Reconnecting += _ =>
        {
            ConnectionStateChanged?.Invoke(ConnectionState.Reconnecting);
            return Task.CompletedTask;
        };

        connection.Reconnected += async _ =>
        {
            ConnectionStateChanged?.Invoke(ConnectionState.Online);
            if (_currentlySelectedChatId.HasValue)
            {
                await JoinChatAsync(_currentlySelectedChatId.Value);
            }
            ReconnectedAndNeedsRefresh?.Invoke();
        };

        connection.Closed += async ex =>
        {
            ConnectionStateChanged?.Invoke(ConnectionState.Offline);
            await StartBackgroundReconnectLoopAsync();
        };

        return connection;
    }

    private async Task StartBackgroundReconnectLoopAsync()
    {
        const int baseDelayMs = 2000;
        const int maxDelayMs = 60_000;
        int attempt = 0;

        // Signal that we are actively retrying (Reconnecting state, not just Offline)
        ConnectionStateChanged?.Invoke(ConnectionState.Reconnecting);

        while (true)
        {
            // Exponential backoff: 2s, 4s, 8s, 16s, 32s, 60s (capped), ±200ms jitter
            int delayMs = (int)Math.Min(baseDelayMs * Math.Pow(2, attempt), maxDelayMs);
            delayMs += Random.Shared.Next(-200, 200);
            delayMs = Math.Max(delayMs, 100); // guard against negative after jitter at attempt=0
            attempt++;

            _logger.LogDebug(
                "SignalR reconnect attempt {Attempt} — waiting {DelayMs}ms before retry",
                attempt, delayMs);

            await Task.Delay(delayMs);

            try
            {
                if (_hubConnection.State == HubConnectionState.Disconnected)
                {
                    await _hubConnection.StartAsync();
                    // Success — hub Reconnected callback fires ConnectionStateChanged(Online)
                    // and re-joins active chat. Our loop is done.
                    _logger.LogDebug("SignalR reconnect attempt {Attempt} succeeded", attempt);
                    return;
                }
                else
                {
                    // Hub reconnected by itself (built-in retry) — loop exits
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SignalR reconnect attempt {Attempt} failed", attempt);
                // Continue loop
            }
        }
    }
}
