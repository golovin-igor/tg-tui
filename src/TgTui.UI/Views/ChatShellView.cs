using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgTui.Core.Models;
using TgTui.UI.ViewModels;

namespace TgTui.UI.Views;

/// <summary>
/// Main chat shell: dialog list (~33%) + message pane + composer + status bar.
/// </summary>
public sealed class ChatShellView : View
{
    private readonly IApplication _app;
    private readonly ChatShellDependencies _deps;
    private readonly DialogListViewModel _dialogVm;
    private readonly MessagePaneViewModel _messageVm;
    private readonly ComposerViewModel _composerVm;
    private readonly DialogListView _dialogList;
    private readonly MessagePaneView _messagePane;
    private readonly ComposerView _composer;
    private readonly StatusBarView _statusBar;
    private readonly Label _header;
    private readonly FrameView _dialogsFrame;
    private readonly FrameView _messagesFrame;
    private readonly FrameView _composerFrame;
    private FocusZone _focusZone = FocusZone.Dialogs;
    private bool _disposed;
    private bool _started;

    public ChatShellView(IApplication app, ChatShellDependencies dependencies)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _deps = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _dialogVm = new DialogListViewModel(_deps.Dialogs, _deps.Updates);
        _messageVm = new MessagePaneViewModel(_deps.Messages, _deps.Media, _deps.Updates);
        _composerVm = new ComposerViewModel(_deps.Messages, _deps.Drafts);

        _statusBar = new StatusBarView(_app, _deps.Updates)
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
        };

        // ~33% left column for dialogs (design §3.2: 30–35%).
        _dialogsFrame = new FrameView
        {
            Title = "Chats",
            X = 0,
            Y = 0,
            Width = Dim.Percent(33),
            Height = Dim.Fill(1),
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        _dialogList = new DialogListView(_app, _dialogVm)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _dialogsFrame.Add(_dialogList);

        var rightX = Pos.Right(_dialogsFrame);
        var rightW = Dim.Fill();

        _header = new Label
        {
            Text = " Select a chat",
            X = rightX,
            Y = 0,
            Width = rightW,
            Height = 1,
            CanFocus = false,
        };

        // Leave 4 rows for composer + 1 for status at the bottom of the shell.
        _messagesFrame = new FrameView
        {
            Title = "Messages",
            X = rightX,
            Y = 1,
            Width = rightW,
            Height = Dim.Fill(5),
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        _messagePane = new MessagePaneView(_app, _messageVm)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _messagesFrame.Add(_messagePane);

        _composerFrame = new FrameView
        {
            Title = "Message",
            X = rightX,
            Y = Pos.AnchorEnd(5),
            Width = rightW,
            Height = 4,
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        _composer = new ComposerView(_app, _composerVm)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _composerFrame.Add(_composer);

        Add(_dialogsFrame, _header, _messagesFrame, _composerFrame, _statusBar);

        _dialogList.DialogOpened += OnDialogOpened;
        _dialogList.ErrorOccurred += OnShellError;
        _messagePane.RequestFocusComposer += OnRequestFocusComposer;
        _messagePane.RequestFocusDialogs += OnRequestFocusDialogs;
        _messagePane.ReplyRequested += OnReplyRequested;
        _messagePane.EditRequested += OnEditRequested;
        _messagePane.ErrorOccurred += OnShellError;
        _messageVm.ChatMarkedRead += OnChatMarkedRead;
        _composer.OptimisticPresented += OnOptimisticPresented;
        _composer.MessageSubmitted += OnMessageSubmitted;
        _composer.SendFailed += OnSendFailed;
        _composer.LeaveRequested += OnComposerLeaveRequested;

        KeyDown += OnShellKeyDown;
        UpdateChrome();
    }

    /// <summary>
    /// Loads drafts + dialogs and starts the update hub. Call once after attach.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            return;
        _started = true;

        try
        {
            await _deps.Drafts.LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            // empty drafts on load failure
        }

        try
        {
            await _deps.Updates.StartAsync(cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            // hub may be unavailable in offline/fake mode
        }

        try
        {
            await _dialogVm.LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Marshal(() => _statusBar.SetContext($"dialogs error: {ex.Message}"));
        }

        Marshal(() => SetFocusZone(FocusZone.Dialogs));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            KeyDown -= OnShellKeyDown;
            _dialogList.DialogOpened -= OnDialogOpened;
            _dialogList.ErrorOccurred -= OnShellError;
            _messagePane.RequestFocusComposer -= OnRequestFocusComposer;
            _messagePane.RequestFocusDialogs -= OnRequestFocusDialogs;
            _messagePane.ReplyRequested -= OnReplyRequested;
            _messagePane.EditRequested -= OnEditRequested;
            _messagePane.ErrorOccurred -= OnShellError;
            _messageVm.ChatMarkedRead -= OnChatMarkedRead;
            _composer.OptimisticPresented -= OnOptimisticPresented;
            _composer.MessageSubmitted -= OnMessageSubmitted;
            _composer.SendFailed -= OnSendFailed;
            _composer.LeaveRequested -= OnComposerLeaveRequested;

            _dialogVm.Dispose();
            _messageVm.Dispose();
            _dialogList.Dispose();
            _messagePane.Dispose();
            _composer.Dispose();
            _statusBar.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnRequestFocusComposer() => SetFocusZone(FocusZone.Composer);

    private void OnRequestFocusDialogs() => SetFocusZone(FocusZone.Dialogs);

    private void OnComposerLeaveRequested() => SetFocusZone(FocusZone.Messages);

    private void OnShellKeyDown(object? sender, Key key)
    {
        if (key == Key.Tab && !key.IsShift)
        {
            SetFocusZone(NextZone(_focusZone));
            key.Handled = true;
            return;
        }

        if (key == Key.Tab.WithShift || (key == Key.Tab && key.IsShift))
        {
            SetFocusZone(PrevZone(_focusZone));
            key.Handled = true;
        }
    }

    private void OnDialogOpened(DialogItem dialog) => _ = OpenDialogAsync(dialog);

    private void OnShellError(string message) =>
        Marshal(() => _statusBar.SetContext(string.IsNullOrWhiteSpace(message) ? "error" : message));

    private void OnChatMarkedRead(ChatId chatId) =>
        Marshal(() => _dialogVm.ClearUnread(chatId));

    private async Task OpenDialogAsync(DialogItem dialog)
    {
        try
        {
            await _messageVm.OpenChatAsync(dialog).ConfigureAwait(true);
            await _composerVm.BindChatAsync(dialog.Id).ConfigureAwait(true);
            Marshal(() =>
            {
                _header.Text = $" {dialog.Title}";
                _messagesFrame.Title = dialog.Title;
                SetFocusZone(FocusZone.Messages);
                UpdateChrome();
            });
        }
        catch (Exception ex)
        {
            Marshal(() => _statusBar.SetContext($"open chat: {ex.Message}"));
        }
    }

    private void OnReplyRequested()
    {
        _composerVm.SetReply(_messageVm.ReplyToId);
        SetFocusZone(FocusZone.Composer);
        UpdateChrome();
    }

    private void OnEditRequested()
    {
        var selected = _messageVm.Selected;
        if (selected is null || !selected.IsOutgoing)
            return;

        _composerVm.BeginEdit(selected.Id, selected.Text);
        SetFocusZone(FocusZone.Composer);
        UpdateChrome();
    }

    private void OnOptimisticPresented(ChatMessage message)
    {
        Marshal(() =>
        {
            _messageVm.PresentOptimistic(message);
            UpdateChrome();
        });
    }

    private void OnMessageSubmitted(ComposerSubmitOutcome outcome)
    {
        Marshal(() =>
        {
            if (outcome.IsEdit)
            {
                if (outcome.EditedMessageId is { } editId && outcome.EditedText is not null)
                    _messageVm.ApplyLocalEdit(editId, outcome.EditedText);
            }
            else if (outcome.OptimisticMessage is { } opt && outcome.ConfirmedMessage is { } confirmed)
            {
                _messageVm.ConfirmOptimistic(opt.Id, confirmed);
            }

            _messageVm.JumpToLatest();
            SetFocusZone(FocusZone.Messages);
            UpdateChrome();
        });
    }

    private void OnSendFailed(MessageId optimisticId)
    {
        Marshal(() =>
        {
            _messageVm.CancelOptimistic(optimisticId);
            SetFocusZone(FocusZone.Composer);
            _statusBar.SetContext("send failed · fix text and retry");
        });
    }

    private void SetFocusZone(FocusZone zone)
    {
        if (_disposed)
            return;

        _focusZone = zone;
        switch (zone)
        {
            case FocusZone.Dialogs:
                _dialogList.FocusList();
                break;
            case FocusZone.Messages:
                _messagePane.FocusList();
                break;
            case FocusZone.Composer:
                _composer.FocusInput();
                break;
        }

        UpdateChrome();
    }

    private void UpdateChrome()
    {
        if (_disposed)
            return;

        var zone = _focusZone switch
        {
            FocusZone.Dialogs => "focus: dialogs",
            FocusZone.Messages => "focus: messages",
            FocusZone.Composer => "focus: composer",
            _ => "",
        };
        _statusBar.SetContext(zone);
    }

    private static FocusZone NextZone(FocusZone z) => z switch
    {
        FocusZone.Dialogs => FocusZone.Messages,
        FocusZone.Messages => FocusZone.Composer,
        _ => FocusZone.Dialogs,
    };

    private static FocusZone PrevZone(FocusZone z) => z switch
    {
        FocusZone.Dialogs => FocusZone.Composer,
        FocusZone.Messages => FocusZone.Dialogs,
        _ => FocusZone.Messages,
    };

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
