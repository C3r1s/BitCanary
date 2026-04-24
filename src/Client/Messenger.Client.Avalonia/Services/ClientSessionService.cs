using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// Manages authentication session state.
/// Priority order: environment variables → persisted session file → unauthenticated.
/// The persisted JWT is DPAPI-protected at rest (SEC-01, purpose "Messenger.SessionToken.v1").
/// </summary>
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
        // SEC-01 (D-02, D-03): dedicated protector — do NOT reuse IKeyStore.
        _sessionProtector = dpProvider.CreateProtector("Messenger.SessionToken.v1");

        var apiBaseUrl = Environment.GetEnvironmentVariable("MESSENGER_API_BASE_URL");
        ApiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "http://localhost:5000"   // matches launchSettings.json http profile
            : apiBaseUrl.TrimEnd('/');

        // Environment variables take precedence (CI / docker injection) — unchanged by SEC-01.
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

            var rawToken = data.AccessToken ?? string.Empty;

            // SEC-01 (D-01): legacy v1.0 plaintext JWTs start with "eyJ" (base64url of {").
            // One-time migration: discard and force re-login.
            if (rawToken.StartsWith("eyJ", StringComparison.Ordinal))
            {
                try { File.Delete(SessionFilePath); } catch { /* best-effort */ }
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
                // Corrupt, tampered, or provider-keyring-rotated blob — discard and force re-login.
                AccessToken   = null;
                CurrentUserId = Guid.Empty;
                UserName      = string.Empty;
                try { File.Delete(SessionFilePath); } catch { /* best-effort */ }
            }
        }
        catch
        {
            // Ignore corrupt or unreadable session files (bad JSON, IO errors, etc.).
        }
    }

    private void PersistSession()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionFilePath)!);

            // SEC-01 (D-02): encrypt the JWT before serialization.
            var protectedToken = Convert.ToBase64String(
                _sessionProtector.Protect(
                    Encoding.UTF8.GetBytes(AccessToken ?? string.Empty)));

            var data = new PersistedSession(CurrentUserId, UserName, protectedToken);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(SessionFilePath, json);
        }
        catch
        {
            // Non-fatal — session works in-memory even if disk write fails.
        }
    }

    // D-04: no version field — legacy detection via eyJ prefix is sufficient.
    private sealed record PersistedSession(Guid UserId, string UserName, string AccessToken);
}
