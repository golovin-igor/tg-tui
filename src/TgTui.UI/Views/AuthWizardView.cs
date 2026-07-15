using System.Globalization;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgTui.Core.Events;
using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.UI.Views;

/// <summary>
/// Full-screen auth wizard driven by <see cref="IAuthService"/> phase.
/// </summary>
public sealed class AuthWizardView : View
{
    private readonly IAuthService _auth;
    private readonly IApplication _app;
    private readonly Action _onReady;
    private readonly IUpdateHub? _hub;

    private readonly Label _heading;
    private readonly Label _prompt;
    private readonly Label _status;

    private readonly Label _apiIdLabel;
    private readonly TextField _apiIdField;
    private readonly Label _apiHashLabel;
    private readonly TextField _apiHashField;
    private readonly Label _phoneLabel;
    private readonly TextField _phoneField;
    private readonly Label _codeLabel;
    private readonly TextField _codeField;
    private readonly Label _passwordLabel;
    private readonly TextField _passwordField;
    private readonly Button _submitButton;
    private readonly Button _retryButton;

    private bool _busy;
    private bool _readyRaised;
    private bool _started;
    private bool _disposed;
    private bool _hasInputPhase;
    private AuthPhase _lastInputPhase = AuthPhase.NeedsCredentials;

    public AuthWizardView(IAuthService auth, IApplication app, Action onReady, IUpdateHub? hub = null)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _onReady = onReady ?? throw new ArgumentNullException(nameof(onReady));
        _hub = hub;

        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _heading = new Label
        {
            Text = "tg-tui — Sign in",
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            CanFocus = false,
        };

        _prompt = new Label
        {
            Text = string.Empty,
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            CanFocus = false,
        };

        _apiIdLabel = CreateFieldLabel("API ID (from my.telegram.org):", y: 4);
        _apiIdField = CreateField(y: 5, secret: false);
        _apiHashLabel = CreateFieldLabel("API hash:", y: 7);
        _apiHashField = CreateField(y: 8, secret: true);

        _phoneLabel = CreateFieldLabel("Phone number (international format):", y: 4);
        _phoneField = CreateField(y: 5, secret: false);

        _codeLabel = CreateFieldLabel("Login code from Telegram:", y: 4);
        _codeField = CreateField(y: 5, secret: false);

        _passwordLabel = CreateFieldLabel("Two-factor / cloud password:", y: 4);
        _passwordField = CreateField(y: 5, secret: true);

        _submitButton = new Button
        {
            Text = "_Continue",
            X = 1,
            Y = 10,
            IsDefault = true,
        };
        _submitButton.Accepting += OnSubmitAccepting;

        _retryButton = new Button
        {
            Text = "_Retry",
            X = 1,
            Y = 10,
            Visible = false,
        };
        _retryButton.Accepting += OnRetryAccepting;

        _status = new Label
        {
            Text = string.Empty,
            X = 1,
            Y = 12,
            Width = Dim.Fill(1),
            Height = 3,
            CanFocus = false,
        };

        Add(
            _heading,
            _prompt,
            _apiIdLabel,
            _apiIdField,
            _apiHashLabel,
            _apiHashField,
            _phoneLabel,
            _phoneField,
            _codeLabel,
            _codeField,
            _passwordLabel,
            _passwordField,
            _submitButton,
            _retryButton,
            _status);

        if (_hub is not null)
            _hub.AuthStateChanged += OnAuthStateChanged;

        // Defer phase UI until StartAsync / events; avoid treating the service's
        // pre-Start NeedsCredentials default as the retry surface after a resume failure.
        SetFieldVisibility(false, false, false, false);
        _prompt.Text = "Starting…";
        _submitButton.Visible = false;
        _retryButton.Visible = false;
    }

    /// <summary>
    /// Loads config / resumes session and refreshes the wizard for the resulting phase.
    /// Safe to call once after the view is attached.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            return;

        _started = true;
        SetBusy(true, "Starting…");
        try
        {
            await _auth.StartAsync(cancellationToken).ConfigureAwait(true);
            MarshalApply(_auth.Current);
        }
        catch (Exception ex)
        {
            MarshalApply(new AuthState(AuthPhase.Failed, ex.Message));
        }
        finally
        {
            MarshalSetBusy(false);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            if (_hub is not null)
                _hub.AuthStateChanged -= OnAuthStateChanged;
        }

        base.Dispose(disposing);
    }

    private void OnAuthStateChanged(AuthStateChanged e)
    {
        MarshalApply(e.State);
    }

    private void OnSubmitAccepting(object? sender, CommandEventArgs e)
    {
        e.Handled = true;
        if (_busy)
            return;

        _ = SubmitCurrentPhaseAsync();
    }

    private void OnRetryAccepting(object? sender, CommandEventArgs e)
    {
        e.Handled = true;
        if (_busy)
            return;

        _started = false;
        _ = StartAsync();
    }

    private async Task SubmitCurrentPhaseAsync()
    {
        var phase = _auth.Current.Phase;
        if (phase == AuthPhase.Failed)
            phase = _lastInputPhase;

        SetBusy(true, "Working…");
        try
        {
            switch (phase)
            {
                case AuthPhase.NeedsCredentials:
                    await SubmitCredentialsAsync().ConfigureAwait(true);
                    break;
                case AuthPhase.NeedsPhone:
                    await _auth.SubmitPhoneAsync(_phoneField.Text ?? string.Empty).ConfigureAwait(true);
                    break;
                case AuthPhase.NeedsCode:
                    await _auth.SubmitCodeAsync(_codeField.Text ?? string.Empty).ConfigureAwait(true);
                    break;
                case AuthPhase.NeedsPassword:
                    await _auth.SubmitPasswordAsync(_passwordField.Text ?? string.Empty).ConfigureAwait(true);
                    break;
            }

            MarshalApply(_auth.Current);
        }
        catch (Exception ex)
        {
            MarshalSetStatus(ex.Message);
        }
        finally
        {
            MarshalSetBusy(false);
        }
    }

    private async Task SubmitCredentialsAsync()
    {
        var rawId = (_apiIdField.Text ?? string.Empty).Trim();
        if (!int.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var apiId) || apiId <= 0)
            throw new ArgumentException("api_id must be a positive integer.");

        var apiHash = _apiHashField.Text ?? string.Empty;
        await _auth.SubmitCredentialsAsync(apiId, apiHash).ConfigureAwait(true);
    }

    private void MarshalApply(AuthState state)
    {
        if (Environment.CurrentManagedThreadId == _app.MainThreadId)
        {
            ApplyState(state);
            return;
        }

        _app.Invoke(() => ApplyState(state));
    }

    private void MarshalSetBusy(bool busy, string? status = null)
    {
        if (Environment.CurrentManagedThreadId == _app.MainThreadId)
        {
            SetBusy(busy, status);
            return;
        }

        _app.Invoke(() => SetBusy(busy, status));
    }

    private void MarshalSetStatus(string? message)
    {
        if (Environment.CurrentManagedThreadId == _app.MainThreadId)
        {
            SetStatus(message);
            return;
        }

        _app.Invoke(() => SetStatus(message));
    }

    private void ApplyState(AuthState state)
    {
        if (_disposed)
            return;

        if (state.Phase is AuthPhase.NeedsCredentials or AuthPhase.NeedsPhone
            or AuthPhase.NeedsCode or AuthPhase.NeedsPassword)
        {
            _hasInputPhase = true;
            _lastInputPhase = state.Phase;
        }

        if (state.Phase == AuthPhase.Ready)
        {
            SetStatus(state.Message ?? "Signed in.");
            RaiseReadyOnce();
            return;
        }

        var isFailed = state.Phase == AuthPhase.Failed;
        var showCredentials = state.Phase == AuthPhase.NeedsCredentials
            || (isFailed && _hasInputPhase && _lastInputPhase == AuthPhase.NeedsCredentials);
        var showPhone = state.Phase == AuthPhase.NeedsPhone
            || (isFailed && _hasInputPhase && _lastInputPhase == AuthPhase.NeedsPhone);
        var showCode = state.Phase == AuthPhase.NeedsCode
            || (isFailed && _hasInputPhase && _lastInputPhase == AuthPhase.NeedsCode);
        var showPassword = state.Phase == AuthPhase.NeedsPassword
            || (isFailed && _hasInputPhase && _lastInputPhase == AuthPhase.NeedsPassword);

        SetFieldVisibility(showCredentials, showPhone, showCode, showPassword);

        _prompt.Text = state.Phase switch
        {
            AuthPhase.NeedsCredentials => "Enter API credentials from https://my.telegram.org",
            AuthPhase.NeedsPhone => "Enter the phone number for your Telegram account",
            AuthPhase.NeedsCode => "Enter the login code Telegram sent you",
            AuthPhase.NeedsPassword => "Enter your two-factor authentication password",
            AuthPhase.Failed => "Authentication failed — fix the issue and continue or retry",
            _ => string.Empty,
        };

        _submitButton.Visible = !isFailed || showCredentials || showPhone || showCode || showPassword;
        _retryButton.Visible = isFailed;
        if (isFailed)
        {
            _retryButton.X = Pos.Right(_submitButton) + 2;
            _retryButton.Y = 10;
        }

        SetStatus(state.Message);

        if (!_busy)
            FocusPrimaryField(state.Phase == AuthPhase.Failed ? _lastInputPhase : state.Phase);
    }

    private void SetFieldVisibility(bool credentials, bool phone, bool code, bool password)
    {
        _apiIdLabel.Visible = credentials;
        _apiIdField.Visible = credentials;
        _apiHashLabel.Visible = credentials;
        _apiHashField.Visible = credentials;

        _phoneLabel.Visible = phone;
        _phoneField.Visible = phone;

        _codeLabel.Visible = code;
        _codeField.Visible = code;

        _passwordLabel.Visible = password;
        _passwordField.Visible = password;
    }

    private void FocusPrimaryField(AuthPhase phase)
    {
        View? target = phase switch
        {
            AuthPhase.NeedsCredentials => _apiIdField,
            AuthPhase.NeedsPhone => _phoneField,
            AuthPhase.NeedsCode => _codeField,
            AuthPhase.NeedsPassword => _passwordField,
            _ => null,
        };

        target?.SetFocus();
    }

    private void SetBusy(bool busy, string? status = null)
    {
        if (_disposed)
            return;

        _busy = busy;
        _submitButton.Enabled = !busy;
        _retryButton.Enabled = !busy;
        _apiIdField.ReadOnly = busy;
        _apiHashField.ReadOnly = busy;
        _phoneField.ReadOnly = busy;
        _codeField.ReadOnly = busy;
        _passwordField.ReadOnly = busy;

        if (status is not null)
            SetStatus(status);
        else if (!busy && string.IsNullOrEmpty(_status.Text))
            SetStatus(null);
    }

    private void SetStatus(string? message)
    {
        if (_disposed)
            return;

        _status.Text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
    }

    private void RaiseReadyOnce()
    {
        if (_readyRaised || _disposed)
            return;

        _readyRaised = true;
        // Defer shell switch so in-flight StartAsync/Submit finally blocks can finish on this view.
        _app.AddTimeout(TimeSpan.Zero, () =>
        {
            if (!_disposed)
                _onReady();
            return false;
        });
    }

    private static Label CreateFieldLabel(string text, int y) =>
        new()
        {
            Text = text,
            X = 1,
            Y = y,
            Width = Dim.Fill(1),
            CanFocus = false,
            Visible = false,
        };

    private static TextField CreateField(int y, bool secret) =>
        new()
        {
            X = 1,
            Y = y,
            Width = Dim.Fill(1),
            Secret = secret,
            Visible = false,
        };
}
