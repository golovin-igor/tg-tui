using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgTui.UI.Theme;
using TgTui.UI.ViewModels;
using ValueChangedEventArgs = Terminal.Gui.App.ValueChangedEventArgs<int?>;

namespace TgTui.UI.Views;

/// <summary>
/// Message list for the open chat (design §3.4 message pane keys).
/// </summary>
public sealed class MessagePaneView : View
{
    private readonly IApplication _app;
    private readonly MessagePaneViewModel _vm;
    private readonly ListView _list;
    private readonly ObservableCollection<string> _rows = [];
    private bool _disposed;
    private bool _syncingSelection;

    public MessagePaneView(IApplication app, MessagePaneViewModel viewModel)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _list.SetSource(_rows);
        _list.ValueChanged += OnListValueChanged;
        _list.KeyDown += OnListKeyDown;
        _list.RowRender += OnRowRender;
        _list.Accepting += OnListAccepting;

        Add(_list);
        _vm.Changed += OnViewModelChanged;
        RefreshRows();
    }

    public event Action? RequestFocusComposer;
    public event Action? RequestFocusDialogs;
    public event Action? ReplyRequested;
    public event Action? EditRequested;

    public void FocusList() => _list.SetFocus();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _vm.Changed -= OnViewModelChanged;
            _list.ValueChanged -= OnListValueChanged;
            _list.KeyDown -= OnListKeyDown;
            _list.RowRender -= OnRowRender;
            _list.Accepting -= OnListAccepting;
        }

        base.Dispose(disposing);
    }

    private void OnViewModelChanged() => Marshal(RefreshRows);

    private void RefreshRows()
    {
        if (_disposed)
            return;

        var width = Math.Max(20, Frame.Width - 2);
        _rows.Clear();
        foreach (var m in _vm.Messages)
            _rows.Add(_vm.FormatRow(m, width));

        _syncingSelection = true;
        try
        {
            if (_rows.Count > 0)
            {
                _list.SelectedItem = _vm.SelectedIndex;
                _list.EnsureSelectedItemVisible();
            }
            else
            {
                _list.SelectedItem = null;
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        _list.SetNeedsDraw();
    }

    private void OnListValueChanged(object? sender, ValueChangedEventArgs<int?> e)
    {
        if (_syncingSelection || _disposed)
            return;
        if (e.NewValue is int idx)
            _vm.SelectedIndex = idx;
    }

    private void OnListAccepting(object? sender, CommandEventArgs e)
    {
        // Enter: open media externally when the selected row has an attachment (design §3.4).
        if (_vm.Selected?.Media is null)
            return;

        e.Handled = true;
        _ = RunAsync(_vm.OpenSelectedMediaExternallyAsync);
    }

    private void OnRowRender(object? sender, ListViewRowEventArgs e)
    {
        if (e.Row < 0 || e.Row >= _vm.Messages.Count)
            return;

        var msg = _vm.Messages[e.Row];
        e.RowAttribute = msg.IsOutgoing
            ? TelegramDesktopTheme.MessageOutgoing
            : TelegramDesktopTheme.MessageIncoming;
    }

    private void OnListKeyDown(object? sender, Key key)
    {
        if (key == Key.J || key == Key.CursorDown)
        {
            _vm.MoveSelection(1);
            key.Handled = true;
            return;
        }

        if (key == Key.K || key == Key.CursorUp)
        {
            _vm.MoveSelection(-1);
            key.Handled = true;
            return;
        }

        if (key == Key.R)
        {
            _vm.BeginReply();
            ReplyRequested?.Invoke();
            key.Handled = true;
            return;
        }

        if (key == Key.E)
        {
            if (_vm.Selected is { IsOutgoing: true })
                EditRequested?.Invoke();
            key.Handled = true;
            return;
        }

        if (key == Key.D)
        {
            _ = RunAsync(_vm.DeleteSelectedAsync);
            key.Handled = true;
            return;
        }

        if (key == Key.O)
        {
            // `o` always means open media externally when present (no-op without media).
            if (_vm.Selected?.Media is not null)
                _ = RunAsync(_vm.OpenSelectedMediaExternallyAsync);
            key.Handled = true;
            return;
        }

        if (key == Key.I || key == Key.A)
        {
            RequestFocusComposer?.Invoke();
            key.Handled = true;
            return;
        }

        if (key == Key.G || key == Key.H)
        {
            // Capital G is jump-to-latest; lowercase g focuses dialogs.
            if (key == Key.G && key.IsShift)
            {
                _vm.JumpToLatest();
                key.Handled = true;
                return;
            }

            RequestFocusDialogs?.Invoke();
            key.Handled = true;
            return;
        }

        // Key.G with shift may arrive as a distinct key instance.
        if (key == Key.G.WithShift)
        {
            _vm.JumpToLatest();
            key.Handled = true;
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        try
        {
            await action(CancellationToken.None).ConfigureAwait(true);
        }
        catch
        {
            // keep UI alive
        }
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
}
