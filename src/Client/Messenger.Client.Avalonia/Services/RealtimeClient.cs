using Messenger.Shared.Contracts.Dtos;
using Messenger.Shared.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;

namespace Messenger.Client.Avalonia.Services;

public sealed class RealtimeClient : IRealtimeClient
{
    private readonly IClientSessionService _sessionService;
    private readonly HubConnection _hubConnection;

    public RealtimeClient(IClientSessionService sessionService)
    {
        _sessionService = sessionService;
        _hubConnection = CreateConnection(sessionService);
    }

    public event Func<MessageDto, Task>? MessageReceived;
    public event Func<TypingIndicatorDto, Task>? TypingReceived;
    public event Func<CallSignalDto, Task>? CallSignalReceived;
    public event Func<PresenceChangedDto, Task>? PresenceChanged;
    public event Func<Task>? OtpkSupplyLow;

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

        return connection;
    }
}
