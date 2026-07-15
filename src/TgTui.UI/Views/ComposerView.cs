using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgTui.Core.Models;
using TgTui.UI.ViewModels;

namespace TgTui.UI.Views;

/// <summary>
/// Multiline message composer with draft persistence and send / cancel-reply keys.
/// </summary>
public sealed class ComposerView : View
{
#pragma warning disable CS0618 // TextView is obsolete in favor of external EditorView; still the in-box multiline editor.
    private readonly TextView _text;
#pragma warning restore CS0618
    private readonly IApplication _app;
    private readonly ComposerViewModel _vm;
    private readonly Label _hint;
    private bool _disposed;
    private bool _syncingText;

    public ComposerView(IApplication app, ComposerViewModel viewModel)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _hint = new Label
        {
            Text = _vm.StatusHint,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
        };

#pragma warning disable CS0618
        _text = new TextView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Multiline = true,
            WordWrap = true,
            EnterKeyAddsLine = false,
            TabKeyAddsTab = false,
            CanFocus = true,
        };
#pragma warning restore CS0618
        _text.ContentsChanged += OnContentsChanged;
        _text.KeyDown += OnTextKeyDown;

        Add(_hint, _text);
        _vm.Changed += OnViewModelChanged;
        SyncFromViewModel();
    }

    /// <summary>
    /// Raised when an optimistic outgoing message should appear in the pane (before network ack).
    /// </summary>
    public event Action<ChatMessage>? OptimisticPresented;

    /// <summary>Raised after a successful send or edit so the shell can reconcile the pane.</summary>
    public event Action<ComposerSubmitOutcome>? MessageSubmitted;

    /// <summary>Raised when a send fails after an optimistic row was shown.</summary>
    public event Action<MessageId>? SendFailed;

    /// <summary>Esc with no reply/edit — leave composer.</summary>
    public event Action? LeaveRequested;

    public void FocusInput() => _text.SetFocus();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _vm.Changed -= OnViewModelChanged;
            _text.ContentsChanged -= OnContentsChanged;
            _text.KeyDown -= OnTextKeyDown;
        }

        base.Dispose(disposing);
    }

    private void OnViewModelChanged() => Marshal(SyncFromViewModel);

    private void SyncFromViewModel()
    {
        if (_disposed)
            return;

        _hint.Text = _vm.StatusHint;
        if (!string.Equals(_text.Text, _vm.Text, StringComparison.Ordinal))
        {
            _syncingText = true;
            try
            {
                _text.Text = _vm.Text;
            }
            finally
            {
                _syncingText = false;
            }
        }
    }

#pragma warning disable CS0618 // ContentsChangedEventArgs tied to obsolete TextView
    private void OnContentsChanged(object? sender, ContentsChangedEventArgs e)
#pragma warning restore CS0618
    {
        if (_syncingText || _disposed)
            return;

        _ = e;
        _vm.Text = _text.Text ?? "";
        _ = PersistDebouncedAsync();
    }

    private void OnTextKeyDown(object? sender, Key key)
    {
        if (key == Key.Enter && !key.IsShift)
        {
            key.Handled = true;
            _ = SubmitAsync();
            return;
        }

        if (key == Key.Enter.WithShift || (key == Key.Enter && key.IsShift))
        {
            key.Handled = true;
            InsertNewline();
            return;
        }

        if (key == Key.Esc)
        {
            key.Handled = true;
            if (_vm.CancelReplyOrEdit())
                return;

            _ = PersistAndLeaveAsync();
        }
    }

    private void InsertNewline()
    {
        var current = _text.Text ?? "";
        _text.Text = current + Environment.NewLine;
        _vm.Text = _text.Text;
    }

    private async Task SubmitAsync()
    {
        try
        {
            _vm.Text = _text.Text ?? "";
            var outcome = await _vm
                .SubmitAsync(presentOptimistic: msg => OptimisticPresented?.Invoke(msg))
                .ConfigureAwait(true);

            if (outcome is not null)
                MessageSubmitted?.Invoke(outcome);
        }
        catch (ComposerSendException ex)
        {
            SendFailed?.Invoke(ex.OptimisticId);
        }
        catch
        {
            // keep composer usable
        }
    }

    private async Task PersistAndLeaveAsync()
    {
        try
        {
            _vm.Text = _text.Text ?? "";
            await _vm.PersistDraftAsync().ConfigureAwait(true);
        }
        catch
        {
            // still leave
        }

        LeaveRequested?.Invoke();
    }

    private async Task PersistDebouncedAsync()
    {
        try
        {
            await Task.Delay(400).ConfigureAwait(true);
            if (_disposed)
                return;
            await _vm.PersistDraftAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore draft IO errors in UI path
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
