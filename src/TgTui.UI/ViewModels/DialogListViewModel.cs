using TgTui.Core.Events;
using TgTui.Core.Filtering;
using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.UI.ViewModels;

/// <summary>
/// Loads, filters, and mutates the dialog list via <see cref="IDialogService"/>.
/// UI must marshal <see cref="Changed"/> onto the Terminal.Gui main thread.
/// </summary>
public sealed class DialogListViewModel : IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly IUpdateHub? _hub;
    private readonly List<DialogItem> _all = [];
    private string _filter = "";
    private int _selectedIndex;
    private ChatId? _activeChatId;
    private bool _disposed;
    private CancellationTokenSource? _reloadCts;

    public DialogListViewModel(IDialogService dialogs, IUpdateHub? hub = null)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _hub = hub;
        if (_hub is not null)
        {
            _hub.DialogsChanged += OnDialogsChanged;
            _hub.MessagesChanged += OnMessagesChanged;
        }
    }

    /// <summary>Raised when the filtered list or selection changes (may be off the UI thread).</summary>
    public event Action? Changed;

    public IReadOnlyList<DialogItem> Items { get; private set; } = Array.Empty<DialogItem>();

    public string Filter
    {
        get => _filter;
        set
        {
            var next = value ?? "";
            if (string.Equals(_filter, next, StringComparison.Ordinal))
                return;
            _filter = next;
            ApplyFilter(preserveSelection: true);
            RaiseChanged();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = ClampIndex(value);
            if (_selectedIndex == next)
                return;
            _selectedIndex = next;
            RaiseChanged();
        }
    }

    public DialogItem? Selected =>
        Items.Count > 0 && _selectedIndex >= 0 && _selectedIndex < Items.Count
            ? Items[_selectedIndex]
            : null;

    /// <summary>Chat currently open in the message pane (suppresses unread bumps).</summary>
    public void SetActiveChatId(ChatId? chatId) => _activeChatId = chatId;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var list = await _dialogs.GetDialogsAsync(cancellationToken).ConfigureAwait(false);
        _all.Clear();
        _all.AddRange(list);
        ApplyFilter(preserveSelection: true);
        RaiseChanged();
    }

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
            return;
        SelectedIndex = _selectedIndex + delta;
    }

    /// <summary>Updates preview/time locally after send/receive without refetching all dialogs.</summary>
    public void ApplyLocalMessage(ChatId chatId, ChatMessage message, bool incrementUnread = false)
    {
        ArgumentNullException.ThrowIfNull(message);
        ApplyMessageUpdate(chatId, message, incrementUnread && !message.IsOutgoing);
    }

    /// <summary>Clears the unread badge for a dialog after mark-as-read (local optimistic).</summary>
    public void ClearUnread(ChatId chatId)
    {
        for (var i = 0; i < _all.Count; i++)
        {
            if (_all[i].Id.Value != chatId.Value || _all[i].UnreadCount == 0)
                continue;

            _all[i] = Clone(_all[i], unreadCount: 0);
            ApplyFilter(preserveSelection: true);
            RaiseChanged();
            return;
        }
    }

    public async Task ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        var item = Selected;
        if (item is null)
            return;

        var muted = !item.IsMuted;
        await _dialogs.SetMutedAsync(item.Id, muted, cancellationToken).ConfigureAwait(false);
        ReplaceItem(Clone(item, isMuted: muted));
        RaiseChanged();
    }

    public async Task TogglePinAsync(CancellationToken cancellationToken = default)
    {
        var item = Selected;
        if (item is null)
            return;

        var pinned = !item.IsPinned;
        await _dialogs.SetPinnedAsync(item.Id, pinned, cancellationToken).ConfigureAwait(false);
        ReplaceItem(Clone(item, isPinned: pinned));
        SortDialogs();
        ApplyFilter(preserveSelection: true);
        RaiseChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
        if (_hub is not null)
        {
            _hub.DialogsChanged -= OnDialogsChanged;
            _hub.MessagesChanged -= OnMessagesChanged;
        }
    }

    private void OnDialogsChanged(DialogsChanged e)
    {
        _ = e;
        ScheduleReloadDebounced();
    }

    private void OnMessagesChanged(MessagesChanged e)
    {
        switch (e.Kind)
        {
            case MessageChangeKind.Added when e.Message is not null:
                ApplyMessageUpdate(e.ChatId, e.Message, incrementUnread: !e.Message.IsOutgoing);
                break;
            case MessageChangeKind.Edited when e.Message is not null:
                ApplyMessageUpdate(e.ChatId, e.Message, incrementUnread: false);
                break;
            case MessageChangeKind.ReadStateChanged when _activeChatId?.Value == e.ChatId.Value:
                ClearUnread(e.ChatId);
                break;
            case MessageChangeKind.Deleted:
                ScheduleReloadDebounced();
                break;
        }
    }

    private void ApplyMessageUpdate(ChatId chatId, ChatMessage message, bool incrementUnread)
    {
        var idx = _all.FindIndex(d => d.Id.Value == chatId.Value);
        if (idx < 0)
        {
            ScheduleReloadDebounced();
            return;
        }

        var item = _all[idx];
        var isActive = _activeChatId?.Value == chatId.Value;
        var unread = item.UnreadCount;
        if (incrementUnread && !isActive)
            unread++;

        _all[idx] = Clone(
            item,
            lastMessagePreview: ChatMessagePreview.FromMessage(message),
            lastMessageAt: message.SentAt,
            unreadCount: isActive ? 0 : unread);

        SortDialogs();
        ApplyFilter(preserveSelection: true);
        RaiseChanged();
    }

    private void ScheduleReloadDebounced()
    {
        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
        _reloadCts = new CancellationTokenSource();
        var token = _reloadCts.Token;
        _ = ReloadDebouncedAsync(token);
    }

    private async Task ReloadDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            await LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer dialogs update burst.
        }
        catch
        {
            // Hub-driven refresh should not crash the process.
        }
    }

    private void ReplaceItem(DialogItem updated)
    {
        for (var i = 0; i < _all.Count; i++)
        {
            if (_all[i].Id.Value == updated.Id.Value)
            {
                _all[i] = updated;
                break;
            }
        }

        ApplyFilter(preserveSelection: true);
    }

    private void SortDialogs()
    {
        _all.Sort(static (a, b) =>
        {
            var pin = b.IsPinned.CompareTo(a.IsPinned);
            if (pin != 0)
                return pin;
            return Nullable.Compare(b.LastMessageAt, a.LastMessageAt);
        });
    }

    private static DialogItem Clone(
        DialogItem item,
        string? lastMessagePreview = null,
        DateTimeOffset? lastMessageAt = null,
        bool? isMuted = null,
        bool? isPinned = null,
        int? unreadCount = null) =>
        new()
        {
            Id = item.Id,
            Title = item.Title,
            LastMessagePreview = lastMessagePreview ?? item.LastMessagePreview,
            LastMessageAt = lastMessageAt ?? item.LastMessageAt,
            UnreadCount = unreadCount ?? item.UnreadCount,
            IsPinned = isPinned ?? item.IsPinned,
            IsMuted = isMuted ?? item.IsMuted,
            AvatarLetter = item.AvatarLetter,
        };

    private void ApplyFilter(bool preserveSelection)
    {
        var selectedId = preserveSelection ? Selected?.Id : null;
        Items = DialogFilter.Apply(_all, _filter);
        if (selectedId is { } id)
        {
            var idx = -1;
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i].Id.Value == id.Value)
                {
                    idx = i;
                    break;
                }
            }

            _selectedIndex = idx >= 0 ? idx : ClampIndex(0);
        }
        else
        {
            _selectedIndex = ClampIndex(_selectedIndex);
        }
    }

    private int ClampIndex(int index)
    {
        if (Items.Count == 0)
            return 0;
        if (index < 0)
            return 0;
        if (index >= Items.Count)
            return Items.Count - 1;
        return index;
    }

    private void RaiseChanged() => Changed?.Invoke();
}
