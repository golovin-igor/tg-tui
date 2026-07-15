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
    private bool _disposed;

    public DialogListViewModel(IDialogService dialogs, IUpdateHub? hub = null)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _hub = hub;
        if (_hub is not null)
            _hub.DialogsChanged += OnDialogsChanged;
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
        // Keep pins near the top like Telegram after a local toggle.
        _all.Sort(static (a, b) =>
        {
            var pin = b.IsPinned.CompareTo(a.IsPinned);
            if (pin != 0)
                return pin;
            return Nullable.Compare(b.LastMessageAt, a.LastMessageAt);
        });
        ApplyFilter(preserveSelection: true);
        RaiseChanged();
    }

    private static DialogItem Clone(DialogItem item, bool? isMuted = null, bool? isPinned = null) =>
        new()
        {
            Id = item.Id,
            Title = item.Title,
            LastMessagePreview = item.LastMessagePreview,
            LastMessageAt = item.LastMessageAt,
            UnreadCount = item.UnreadCount,
            IsPinned = isPinned ?? item.IsPinned,
            IsMuted = isMuted ?? item.IsMuted,
            AvatarLetter = item.AvatarLetter,
        };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_hub is not null)
            _hub.DialogsChanged -= OnDialogsChanged;
    }

    private void OnDialogsChanged(DialogsChanged e)
    {
        _ = e;
        _ = ReloadQuietAsync();
    }

    private async Task ReloadQuietAsync()
    {
        try
        {
            await LoadAsync().ConfigureAwait(false);
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
