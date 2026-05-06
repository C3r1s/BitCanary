// Сервис клиента BitCanary: сеть, кэш, медиа — «ClientSessionService».
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Messenger.Client.Avalonia.Services;

public sealed class ClientSessionService : IClientSessionService
{
    private static readonly string SessionFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Messenger.Client.Avalonia",
        "session.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _sessionProtector;

    public string ApiBaseUrl { get; }
    public string? AccessToken { get; private set; }
    public Guid CurrentUserId { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken) && CurrentUserId != Guid.Empty;

    public ClientSessionService(IDataProtectionProvider dpProvider)
    {
        _sessionProtector = dpProvider.CreateProtector("Messenger.SessionToken.v1");

        var apiBaseUrl = Environment.GetEnvironmentVariable("MESSENGER_API_BASE_URL");
        ApiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "http://localhost:5000"   // matches launchSettings.json http profile
            : apiBaseUrl.TrimEnd('/');

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
        catch {  }
    }


    private void TryLoadPersistedSession()
    {
        try
        {
            if (!File.Exists(SessionFilePath)) return;

            var json = File.ReadAllText(SessionFilePath);
            var data = JsonSerializer.Deserialize<PersistedSession>(json, JsonOptions);
            if (data is null) return;

            var rawToken = data.AccessToken ?? string.Empty;

            if (rawToken.StartsWith("eyJ", StringComparison.Ordinal))
            {
                try { File.Delete(SessionFilePath); } catch {  }
                return;
            }

            try
            {
                AccessToken   = Encoding.UTF8.GetString(
                    _sessionProtector.Unprotect(Convert.FromBase64String(rawToken)));
                CurrentUserId = data.UserId;
                UserName      = data.UserName;
            }
            catch
            {
                AccessToken   = null;
                CurrentUserId = Guid.Empty;
                UserName      = string.Empty;
                try { File.Delete(SessionFilePath); } catch {  }
            }
        }
        catch
        {
        }
    }

    private void PersistSession()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionFilePath)!);

            var protectedToken = Convert.ToBase64String(
                _sessionProtector.Protect(
                    Encoding.UTF8.GetBytes(AccessToken ?? string.Empty)));

            var data = new PersistedSession(CurrentUserId, UserName, protectedToken);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(SessionFilePath, json);
        }
        catch
        {
        }
    }

    private sealed record PersistedSession(Guid UserId, string UserName, string AccessToken);
}
