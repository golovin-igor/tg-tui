using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgTui.Core.Models;
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
    private readonly Label _editBanner;
    private readonly ListView _list;
    private readonly TextField _searchField;
    private readonly Label _searchLabel;
    private readonly ObservableCollection<string> _rows = [];
    private bool _searchOpen;
    private bool _disposed;
    private bool _syncingSelection;

    public MessagePaneView(IApplication app, MessagePaneViewModel viewModel)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _editBanner = new Label
        {
            Text = "",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
            Visible = false,
        };

        _searchLabel = new Label
        {
            Text = " / search in chat",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            CanFocus = false,
            Visible = false,
        };

        _searchField = new TextField
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Visible = false,
            CanFocus = true,
        };
        _searchField.TextChanged += OnSearchTextChanged;
        _searchField.KeyDown += OnSearchKeyDown;

        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _list.ValueChanged += OnListValueChanged;
        _list.KeyDown += OnListKeyDown;
        _list.RowRender += OnRowRender;
        _list.Accepting += OnListAccepting;

        Add(_editBanner, _searchLabel, _searchField, _list);
        _vm.Changed += OnViewModelChanged;
        RefreshRows();
    }

    public event Action? RequestFocusComposer;
    public event Action? RequestFocusDialogs;
    public event Action? ReplyRequested;
    public event Action? EditRequested;

    /// <summary>Raised when delete / media open fails.</summary>
    public event Action<string>? ErrorOccurred;

    public void FocusList()
    {
        if (_searchOpen)
            _searchField.SetFocus();
        else
            _list.SetFocus();
    }

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
            _searchField.TextChanged -= OnSearchTextChanged;
            _searchField.KeyDown -= OnSearchKeyDown;
        }

        base.Dispose(disposing);
    }

    private void OnViewModelChanged() => Marshal(RefreshRows);

    private void RefreshRows()
    {
        if (_disposed)
            return;

        SyncEditBanner();

        var width = Math.Max(20, Frame.Width - 2);
        _rows.Clear();
        for (var i = 0; i < _vm.Messages.Count; i++)
            _rows.Add(_vm.FormatRow(_vm.Messages[i], width, i));

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
        if (_vm.Selected is not { } message)
            return;

        e.Handled = true;
        _ = ShowDetailAsync(message);
    }

    private async Task ShowDetailAsync(ChatMessage message)
    {
        try
        {
            var width = Math.Max(40, Frame.Width - 4);
            var content = await _vm.BuildDetailContentAsync(message, width).ConfigureAwait(true);
            MessageDetailDialog.Show(
                _app,
                content,
                message,
                message.Media is not null ? _vm.OpenSelectedMediaExternallyAsync : null);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    private void OnRowRender(object? sender, ListViewRowEventArgs e)
    {
        if (e.Row < 0 || e.Row >= _vm.Messages.Count)
            return;

        var msg = _vm.Messages[e.Row];
        if (_vm.EditingMessageId is { } editId && msg.Id.Value == editId.Value)
        {
            e.RowAttribute = TelegramDesktopTheme.MessageEditing;
            return;
        }

        e.RowAttribute = msg.IsOutgoing
            ? TelegramDesktopTheme.MessageOutgoing
            : TelegramDesktopTheme.MessageIncoming;
    }

    private void OnListKeyDown(object? sender, Key key)
    {
        if (_searchOpen)
            return;

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
            return;
        }

        if (key == Key.N && key.IsShift)
        {
            _vm.MoveSearchMatch(-1);
            key.Handled = true;
            return;
        }

        if (key == Key.N && !key.IsShift)
        {
            if (_vm.IsSearchActive)
            {
                _vm.MoveSearchMatch(1);
                key.Handled = true;
            }
            return;
        }

        if (key.AsRune.Value == '/' || key == (Key)'/')
        {
            OpenSearch();
            key.Handled = true;
        }
    }

    private void OnSearchTextChanged(object? sender, EventArgs e)
    {
        if (!_searchOpen)
            return;
        _vm.SearchQuery = _searchField.Text ?? "";
    }

    private void OnSearchKeyDown(object? sender, Key key)
    {
        if (key == Key.Esc)
        {
            CloseSearch(clear: true);
            key.Handled = true;
            return;
        }

        if (key == Key.Enter)
        {
            CloseSearch(clear: false);
            key.Handled = true;
            return;
        }

        if (key == Key.N && key.IsShift)
        {
            _vm.MoveSearchMatch(-1);
            key.Handled = true;
            return;
        }

        if (key == Key.N)
        {
            _vm.MoveSearchMatch(1);
            key.Handled = true;
        }
    }

    private void OpenSearch()
    {
        _searchOpen = true;
        _searchField.Visible = true;
        _searchLabel.Visible = false;
        _searchField.Text = _vm.SearchQuery;
        LayoutChrome();
        _searchField.SetFocus();
        SetNeedsLayout();
    }

    private void CloseSearch(bool clear)
    {
        if (!_searchOpen)
            return;

        _searchOpen = false;
        if (clear)
            _vm.ClearSearch();

        _searchField.Visible = false;
        _searchLabel.Visible = false;
        LayoutChrome();
        _list.SetFocus();
        SetNeedsLayout();
    }

    private void SyncEditBanner()
    {
        if (_vm.EditingMessageId is { } id)
        {
            _editBanner.Text = $" ✎ Editing #{id.Value} — apply in composer · Esc cancels";
            _editBanner.Visible = true;
        }
        else
        {
            _editBanner.Text = "";
            _editBanner.Visible = false;
        }

        LayoutChrome();
    }

    private void LayoutChrome()
    {
        var top = 0;
        if (_editBanner.Visible)
        {
            _editBanner.X = 0;
            _editBanner.Y = 0;
            _editBanner.Width = Dim.Fill();
            top = 1;
        }

        if (_searchOpen)
        {
            _searchField.X = 0;
            _searchField.Y = top;
            _searchField.Width = Dim.Fill();
            _list.X = 0;
            _list.Y = top + 1;
            _list.Width = Dim.Fill();
            _list.Height = Dim.Fill();
        }
        else
        {
            _list.X = 0;
            _list.Y = top;
            _list.Width = Dim.Fill();
            _list.Height = Dim.Fill();
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        try
        {
            await action(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
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
