// Состояние и команды UI BitCanary для «LoginViewModel».
using System.Net;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Services;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly IMessengerApiClient _api;
    private readonly IClientSessionService _session;


    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private bool _isRegistering;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;


    public IAsyncRelayCommand SubmitCommand { get; }
    public IRelayCommand ToggleModeCommand { get; }


    public event Func<AuthResponse, Task>? LoginSucceeded;


    public LoginViewModel(IMessengerApiClient api, IClientSessionService session)
    {
        _api     = api;
        _session = session;

        SubmitCommand     = new AsyncRelayCommand(SubmitAsync, CanSubmit);
        ToggleModeCommand = new RelayCommand(ToggleMode);
    }


    partial void OnUserNameChanged(string value)    => SubmitCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value)    => SubmitCommand.NotifyCanExecuteChanged();
    partial void OnDisplayNameChanged(string value) => SubmitCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value)        => SubmitCommand.NotifyCanExecuteChanged();

    private bool CanSubmit()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
            return false;

        return !IsRegistering || !string.IsNullOrWhiteSpace(DisplayName);
    }

    private void ToggleMode()
    {
        IsRegistering = !IsRegistering;
        ErrorMessage  = string.Empty;
        SubmitCommand.NotifyCanExecuteChanged();
    }

    private async Task SubmitAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy       = true;

        try
        {
            AuthResponse auth;

            if (IsRegistering)
            {
                var publicKey = GeneratePublicKey();

                auth = await _api.RegisterAsync(
                    new RegisterRequest(
                        UserName.Trim(),
                        DisplayName.Trim(),
                        Password,
                        publicKey));
            }
            else
            {
                auth = await _api.LoginAsync(
                    new LoginRequest(UserName.Trim(), Password));
            }

            _session.SetSession(auth.UserId, auth.UserName, auth.AccessToken);

            if (LoginSucceeded is not null)
            {
                await LoginSucceeded(auth);
            }
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = ex.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Invalid username or password.",
                HttpStatusCode.Conflict     => "Username is already taken.",
                HttpStatusCode.BadRequest   => "Check your input and try again.",
                null                        => "Cannot connect to server. Is the backend running?",
                _                           => $"Server error ({(int)ex.StatusCode.Value})."
            };
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }


    private static string GeneratePublicKey()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo());
    }
}
