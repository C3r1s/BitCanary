using System.Text.Json;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// Manages authentication session state.
/// Priority order: environment variables → persisted session file → unauthenticated.
/// </summary>
public sealed class ClientSessionService : IClientSessionService
{
    private static readonly string SessionFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Messenger.Client.Avalonia",
        "session.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ApiBaseUrl { get; }
    public string? AccessToken { get; private set; }
    public Guid CurrentUserId { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken) && CurrentUserId != Guid.Empty;

    public ClientSessionService()
    {
        var apiBaseUrl = Environment.GetEnvironmentVariable("MESSENGER_API_BASE_URL");
        ApiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "http://localhost:5176"   // matches launchSettings.json http profile
            : apiBaseUrl.TrimEnd('/');

        // Environment variables take precedence (CI / docker injection)
        var envToken  = Environment.GetEnvironmentVariable("MESSENGER_ACCESS_TOKEN");
        var envUserId = Environment.GetEnvironmentVariable("MESSENGER_USER_ID");
        var envUser   = Environment.GetEnvironmentVariable("MESSENGER_USERNAME");

        if (!string.IsNullOrWhiteSpace(envToken) && Guid.TryParse(envUserId, out var envGuid))
        {
            AccessToken   = envToken;
            CurrentUserId = envGuid;
            UserName      = string.IsNullOrWhiteSpace(envUser) ? "env-user" : envUser;
            return;
        }

        TryLoadPersistedSession();
    }

    public void SetSession(Guid userId, string userName, string accessToken)
    {
        CurrentUserId = userId;
        UserName      = userName;
        AccessToken   = accessToken;
        PersistSession();
    }

    public void ClearSession()
    {
        CurrentUserId = Guid.Empty;
        UserName      = string.Empty;
        AccessToken   = null;

        try { if (File.Exists(SessionFilePath)) File.Delete(SessionFilePath); }
        catch { /* best-effort */ }
    }

    // ------------------------------------------------------------------

    private void TryLoadPersistedSession()
    {
        try
        {
            if (!File.Exists(SessionFilePath)) return;

            var json = File.ReadAllText(SessionFilePath);
            var data = JsonSerializer.Deserialize<PersistedSession>(json, JsonOptions);
            if (data is null) return;

            AccessToken   = data.AccessToken;
            CurrentUserId = data.UserId;
            UserName      = data.UserName;
        }
        catch
        {
            // Ignore corrupt or unreadable session files
        }
    }

    private void PersistSession()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionFilePath)!);
            var data = new PersistedSession(CurrentUserId, UserName, AccessToken ?? string.Empty);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(SessionFilePath, json);
        }
        catch
        {
            // Non-fatal — session works in-memory even if disk write fails
        }
    }

    private sealed record PersistedSession(Guid UserId, string UserName, string AccessToken);
}
