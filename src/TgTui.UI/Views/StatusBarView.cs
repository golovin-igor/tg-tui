using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgTui.Core.Events;
using TgTui.Core.Ports;
using AppKeys = TgTui.UI.Keymap.KeyBindings;

namespace TgTui.UI.Views;

/// <summary>
/// Bottom chrome: connection state from <see cref="IUpdateHub"/> plus global key hints.
/// </summary>
public sealed class StatusBarView : View
{
    private readonly IApplication _app;
    private readonly IUpdateHub? _hub;
    private readonly Label _label;
    private bool _disposed;
    private bool _connectionKnown;
    private bool _isConnected;
    private string? _detail;
    private string _context = "";

    public StatusBarView(IApplication app, IUpdateHub? hub = null)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _hub = hub;

        Width = Dim.Fill();
        Height = 1;
        CanFocus = false;

        _label = new Label
        {
            Text = Format(),
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            CanFocus = false,
        };
        Add(_label);

        if (_hub is not null)
            _hub.ConnectionStateChanged += OnConnectionStateChanged;
    }

    /// <summary>Optional right-side / contextual hint (focus zone, reply, etc.).</summary>
    public void SetContext(string? context)
    {
        _context = context ?? "";
        RefreshLabel();
    }

    public void SetConnection(bool isConnected, string? detail = null)
    {
        _connectionKnown = true;
        _isConnected = isConnected;
        _detail = detail;
        RefreshLabel();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            if (_hub is not null)
                _hub.ConnectionStateChanged -= OnConnectionStateChanged;
        }

        base.Dispose(disposing);
    }

    private void OnConnectionStateChanged(ConnectionStateChanged e)
    {
        Marshal(() => SetConnection(e.IsConnected, e.Detail));
    }

    private void Marshal(Action action)
    {
        if (Environment.CurrentManagedThreadId == _app.MainThreadId)
        {
            action();
            return;
        }

        _app.Invoke(action);
    }

    private void RefreshLabel()
    {
        if (_disposed)
            return;
        _label.Text = Format();
    }

    private string Format()
    {
        var conn = !_connectionKnown
            ? "connecting…"
            : _isConnected
                ? "online"
                : "offline";
        if (_connectionKnown && !string.IsNullOrWhiteSpace(_detail))
            conn = $"{conn} ({_detail})";

        var left = $" {conn}";
        var right = string.IsNullOrWhiteSpace(_context)
            ? AppKeys.StatusHint
            : $"{_context} · {AppKeys.StatusHint}";
        return $"{left}  │  {right}";
    }
}
