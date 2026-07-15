using System.Collections.ObjectModel;
using System.Globalization;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgTui.Core.Models;
using TgTui.UI.ViewModels;
using ValueChangedEventArgs = Terminal.Gui.App.ValueChangedEventArgs<int?>;

namespace TgTui.UI.Views;

/// <summary>
/// Left pane: dialog list with filter, mute/pin, open-chat actions (design §3.4).
/// </summary>
public sealed class DialogListView : View
{
    private readonly IApplication _app;
    private readonly DialogListViewModel _vm;
    private readonly ListView _list;
    private readonly TextField _filterField;
    private readonly Label _filterLabel;
    private readonly ObservableCollection<string> _rows = [];
    private bool _filterOpen;
    private bool _disposed;
    private bool _syncingSelection;

    public DialogListView(IApplication app, DialogListViewModel viewModel)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _filterLabel = new Label
        {
            Text = " / filter",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            CanFocus = false,
            Visible = false,
        };

        _filterField = new TextField
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Visible = false,
            CanFocus = true,
        };
        _filterField.TextChanged += OnFilterTextChanged;
        _filterField.KeyDown += OnFilterKeyDown;

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
        _list.Accepting += OnListAccepting;
        _list.KeyDown += OnListKeyDown;

        Add(_filterLabel, _filterField, _list);
        _vm.Changed += OnViewModelChanged;
        RefreshRows();
    }

    /// <summary>Raised when the user opens the selected dialog (Enter / l).</summary>
    public event Action<DialogItem>? DialogOpened;

    /// <summary>Called by the shell when this zone gains focus.</summary>
    public void FocusList()
    {
        if (_filterOpen)
            _filterField.SetFocus();
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
            _filterField.TextChanged -= OnFilterTextChanged;
            _filterField.KeyDown -= OnFilterKeyDown;
            _list.ValueChanged -= OnListValueChanged;
            _list.Accepting -= OnListAccepting;
            _list.KeyDown -= OnListKeyDown;
        }

        base.Dispose(disposing);
    }

    private void OnViewModelChanged()
    {
        Marshal(RefreshRows);
    }

    private void RefreshRows()
    {
        if (_disposed)
            return;

        _rows.Clear();
        foreach (var d in _vm.Items)
            _rows.Add(FormatDialog(d));

        _syncingSelection = true;
        try
        {
            if (_rows.Count > 0)
                _list.SelectedItem = _vm.SelectedIndex;
            else
                _list.SelectedItem = null;
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
        e.Handled = true;
        OpenSelected();
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

        if (key == Key.L || key == Key.Enter)
        {
            OpenSelected();
            key.Handled = true;
            return;
        }

        if (key == Key.M)
        {
            _ = RunAsync(_vm.ToggleMuteAsync);
            key.Handled = true;
            return;
        }

        if (key == Key.P)
        {
            _ = RunAsync(_vm.TogglePinAsync);
            key.Handled = true;
            return;
        }

        if (key.AsRune.Value == '/' || key == (Key)'/')
        {
            OpenFilter();
            key.Handled = true;
        }
    }

    private void OnFilterTextChanged(object? sender, EventArgs e)
    {
        if (!_filterOpen)
            return;
        _vm.Filter = _filterField.Text ?? "";
    }

    private void OnFilterKeyDown(object? sender, Key key)
    {
        if (key == Key.Esc)
        {
            CloseFilter(clear: false);
            key.Handled = true;
            return;
        }

        if (key == Key.Enter)
        {
            CloseFilter(clear: false);
            OpenSelected();
            key.Handled = true;
        }
    }

    private void OpenFilter()
    {
        _filterOpen = true;
        _filterField.Visible = true;
        _filterLabel.Visible = false;
        _list.Y = 1;
        _list.Height = Dim.Fill();
        _filterField.Text = _vm.Filter;
        _filterField.SetFocus();
        SetNeedsLayout();
    }

    private void CloseFilter(bool clear)
    {
        if (!_filterOpen)
            return;

        _filterOpen = false;
        if (clear)
        {
            _filterField.Text = "";
            _vm.Filter = "";
        }

        _filterField.Visible = false;
        _list.Y = 0;
        _list.Height = Dim.Fill();
        _list.SetFocus();
        SetNeedsLayout();
    }

    private void OpenSelected()
    {
        var item = _vm.Selected;
        if (item is not null)
            DialogOpened?.Invoke(item);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        try
        {
            await action(CancellationToken.None).ConfigureAwait(true);
        }
        catch
        {
            // Surface stays silent; status bar can be extended later for errors.
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

    private static string FormatDialog(DialogItem d)
    {
        var pin = d.IsPinned ? "*" : " ";
        var mute = d.IsMuted ? "🔇" : " ";
        var time = d.LastMessageAt is { } at
            ? at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
            : "     ";
        var badge = d.UnreadCount > 0
            ? (d.UnreadCount > 99 ? "[99+]" : $"[{d.UnreadCount}]")
            : "";
        var preview = string.IsNullOrWhiteSpace(d.LastMessagePreview)
            ? ""
            : " · " + d.LastMessagePreview.Replace('\n', ' ');
        if (preview.Length > 28)
            preview = preview[..27] + "…";

        return $"{pin}{mute}{d.AvatarLetter} {d.Title}{preview}  {time} {badge}".TrimEnd();
    }
}
