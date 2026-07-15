using System.Globalization;
using TgTui.Core.Auth;
using TgTui.Core.Config;
using TgTui.Core.Events;
using TgTui.Core.Models;
using TgTui.Core.Paths;
using TgTui.Core.Ports;
using WTelegram;

namespace TgTui.Telegram;

public sealed class WTelegramAuthService : IAuthService
{
    private readonly AppPaths _paths;
    private readonly ConfigStore _configStore;
    private readonly TelegramSession _session;
    private readonly WTelegramUpdateHub _hub;
    private readonly Func<AppPaths, Func<string, string?>, Client> _clientFactory;
    private readonly Func<Client, string?, CancellationToken, Task<string?>> _login;

    private string? _phone;
    private string? _code;
    private string? _password;
    private AuthPhase _retryPhase = AuthPhase.NeedsCredentials;

    public WTelegramAuthService(
        AppPaths paths,
        ConfigStore configStore,
        TelegramSession session,
        WTelegramUpdateHub hub)
        : this(paths, configStore, session, hub, TelegramClientFactory.Create, DefaultLogin)
    {
    }

    /// <summary>
    /// Testable constructor — inject client factory and login function to avoid network.
    /// </summary>
    public WTelegramAuthService(
        AppPaths paths,
        ConfigStore configStore,
        TelegramSession session,
        WTelegramUpdateHub hub,
        Func<AppPaths, Func<string, string?>, Client> clientFactory,
        Func<Client, string?, CancellationToken, Task<string?>> login)
    {
        _paths = paths;
        _configStore = configStore;
        _session = session;
        _hub = hub;
        _clientFactory = clientFactory;
        _login = login;
        Current = new AuthState(AuthPhase.NeedsCredentials);
    }

    public AuthState Current { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (!_configStore.Current.HasCredentials)
        {
            SetState(AuthPhase.NeedsCredentials);
            return;
        }

        EnsureClient();

        try
        {
            // Resume session if possible (null input asks WTelegram what is needed next).
            var needed = await _login(_session.RequireClient(), null, cancellationToken).ConfigureAwait(false);
            ApplyLoginResult(needed);
        }
        catch (Exception ex)
        {
            Fail(AuthPhase.NeedsPhone, ex.Message);
        }
    }

    public async Task SubmitCredentialsAsync(int apiId, string apiHash, CancellationToken cancellationToken = default)
    {
        if (apiId <= 0)
            throw new ArgumentOutOfRangeException(nameof(apiId), "api_id must be positive.");
        if (string.IsNullOrWhiteSpace(apiHash))
            throw new ArgumentException("api_hash is required.", nameof(apiHash));

        _configStore.Current.ApiId = apiId;
        _configStore.Current.ApiHash = apiHash.Trim();
        await _configStore.SaveAsync(cancellationToken).ConfigureAwait(false);

        _phone = null;
        _code = null;
        _password = null;
        EnsureClient(forceRecreate: true);

        var next = AuthFlow.NextPhase(Current.Phase, AuthEvent.CredentialsSubmitted);
        SetState(next);
        _retryPhase = AuthPhase.NeedsPhone;
    }

    public async Task SubmitPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Phone number is required.", nameof(phone));

        _phone = phone.Trim();
        _code = null;
        _password = null;
        EnsureClient();

        try
        {
            var needed = await _login(_session.RequireClient(), _phone, cancellationToken).ConfigureAwait(false);
            if (needed is null)
            {
                SetState(AuthPhase.Ready);
                return;
            }

            if (IsCodeRequest(needed))
            {
                SetState(AuthPhase.NeedsCode);
                _retryPhase = AuthPhase.NeedsCode;
                return;
            }

            if (IsPasswordRequest(needed))
            {
                SetState(AuthPhase.NeedsPassword);
                _retryPhase = AuthPhase.NeedsPassword;
                return;
            }

            // Unexpected next step (e.g. signup name) — surface as failed with hint.
            Fail(AuthPhase.NeedsPhone, $"Login requires unexpected step: {needed}");
        }
        catch (Exception ex)
        {
            Fail(AuthPhase.NeedsPhone, ex.Message);
        }
    }

    public async Task SubmitCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Verification code is required.", nameof(code));

        _code = code.Trim();
        EnsureClient();

        try
        {
            var needed = await _login(_session.RequireClient(), _code, cancellationToken).ConfigureAwait(false);
            if (needed is null)
            {
                SetState(AuthPhase.Ready);
                return;
            }

            if (IsPasswordRequest(needed))
            {
                SetState(AuthPhase.NeedsPassword);
                _retryPhase = AuthPhase.NeedsPassword;
                return;
            }

            Fail(AuthPhase.NeedsCode, $"Login requires unexpected step: {needed}");
        }
        catch (Exception ex)
        {
            Fail(AuthPhase.NeedsCode, ex.Message);
        }
    }

    public async Task SubmitPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.", nameof(password));

        _password = password;
        EnsureClient();

        try
        {
            var needed = await _login(_session.RequireClient(), _password, cancellationToken).ConfigureAwait(false);
            if (needed is null)
            {
                SetState(AuthPhase.Ready);
                return;
            }

            Fail(AuthPhase.NeedsPassword, $"Login requires unexpected step: {needed}");
        }
        catch (Exception ex)
        {
            Fail(AuthPhase.NeedsPassword, ex.Message);
        }
    }

    private void ApplyLoginResult(string? needed)
    {
        if (needed is null)
        {
            SetState(AuthPhase.Ready);
            return;
        }

        if (IsCodeRequest(needed))
        {
            SetState(AuthPhase.NeedsCode);
            _retryPhase = AuthPhase.NeedsCode;
            return;
        }

        if (IsPasswordRequest(needed))
        {
            SetState(AuthPhase.NeedsPassword);
            _retryPhase = AuthPhase.NeedsPassword;
            return;
        }

        // Not logged in; need phone (or session empty).
        SetState(AuthPhase.NeedsPhone);
        _retryPhase = AuthPhase.NeedsPhone;
    }

    private void EnsureClient(bool forceRecreate = false)
    {
        if (!forceRecreate && _session.Client is not null)
            return;

        if (!_configStore.Current.HasCredentials)
            throw new InvalidOperationException("API credentials are not configured.");

        var client = _clientFactory(_paths, ConfigCallback);
        _session.SetClient(client);
    }

    private string? ConfigCallback(string what) =>
        what switch
        {
            "api_id" => _configStore.Current.ApiId?.ToString(CultureInfo.InvariantCulture),
            "api_hash" => _configStore.Current.ApiHash,
            "phone_number" => _phone,
            "verification_code" => _code,
            "password" => _password,
            _ => null
        };

    private void SetState(AuthPhase phase, string? message = null)
    {
        if (phase != AuthPhase.Failed)
            _retryPhase = phase;

        Current = new AuthState(phase, message);
        _hub.Publish(new AuthStateChanged(Current));
    }

    private void Fail(AuthPhase retryPhase, string message)
    {
        _retryPhase = retryPhase;
        Current = new AuthState(AuthPhase.Failed, message);
        _hub.Publish(new AuthStateChanged(Current));
    }

    private static bool IsCodeRequest(string needed) =>
        needed is "verification_code" or "phone_number";

    private static bool IsPasswordRequest(string needed) =>
        needed is "password";

    private static Task<string?> DefaultLogin(Client client, string? input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // WTelegram Login: null/empty starts or resumes; non-null supplies the last requested field.
        return client.Login(input ?? string.Empty);
    }
}
