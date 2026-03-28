using System.Collections.Concurrent;

namespace Messenger.Api.Services;

public sealed class ConnectionMappingService
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _connections = new();

    public void Add(Guid userId, string connectionId)
    {
        var userConnections = _connections.GetOrAdd(userId, static _ => new ConcurrentDictionary<string, byte>());
        userConnections.TryAdd(connectionId, 0);
    }

    public void Remove(Guid userId, string connectionId)
    {
        if (!_connections.TryGetValue(userId, out var userConnections))
        {
            return;
        }

        userConnections.TryRemove(connectionId, out _);

        if (userConnections.IsEmpty)
        {
            _connections.TryRemove(userId, out _);
        }
    }

    public IReadOnlyCollection<string> GetConnections(Guid userId)
    {
        if (!_connections.TryGetValue(userId, out var userConnections))
        {
            return Array.Empty<string>();
        }

        return userConnections.Keys.ToArray();
    }
}
