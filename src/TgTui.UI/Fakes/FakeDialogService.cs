using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.UI.Fakes;

/// <summary>
/// In-memory dialogs for offline UI development (<c>TG_TUI_FAKE=1</c>).
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    private readonly List<DialogItem> _dialogs;
    private readonly object _gate = new();

    public FakeDialogService()
    {
        var now = DateTimeOffset.Now;
        _dialogs =
        [
            new DialogItem
            {
                Id = new ChatId(1),
                Title = "Alice",
                LastMessagePreview = "See you at 5?",
                LastMessageAt = now.AddMinutes(-12),
                UnreadCount = 2,
                IsPinned = true,
                IsMuted = false,
                AvatarLetter = 'A',
            },
            new DialogItem
            {
                Id = new ChatId(2),
                Title = "Work Group",
                LastMessagePreview = "Bob: deploy went fine",
                LastMessageAt = now.AddHours(-1),
                UnreadCount = 12,
                IsPinned = false,
                IsMuted = true,
                AvatarLetter = 'W',
            },
            new DialogItem
            {
                Id = new ChatId(3),
                Title = "Carol",
                LastMessagePreview = "Thanks!",
                LastMessageAt = now.AddDays(-1),
                UnreadCount = 0,
                IsPinned = false,
                IsMuted = false,
                AvatarLetter = 'C',
            },
            new DialogItem
            {
                Id = new ChatId(4),
                Title = "Saved Messages",
                LastMessagePreview = "shopping list",
                LastMessageAt = now.AddDays(-2),
                UnreadCount = 0,
                IsPinned = true,
                IsMuted = false,
                AvatarLetter = 'S',
            },
            new DialogItem
            {
                Id = new ChatId(5),
                Title = "Design Team",
                LastMessagePreview = "New mockups attached",
                LastMessageAt = now.AddMinutes(-45),
                UnreadCount = 1,
                IsPinned = false,
                IsMuted = false,
                AvatarLetter = 'D',
            },
        ];
    }

    public Task<IReadOnlyList<DialogItem>> GetDialogsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DialogItem>>(_dialogs.ToList());
        }
    }

    public Task SetMutedAsync(ChatId chatId, bool muted, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Replace(chatId, d => Clone(d, isMuted: muted));
        }

        return Task.CompletedTask;
    }

    public Task SetPinnedAsync(ChatId chatId, bool pinned, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Replace(chatId, d => Clone(d, isPinned: pinned));
            _dialogs.Sort(static (a, b) =>
            {
                var pin = b.IsPinned.CompareTo(a.IsPinned);
                if (pin != 0)
                    return pin;
                return Nullable.Compare(b.LastMessageAt, a.LastMessageAt);
            });
        }

        return Task.CompletedTask;
    }

    private void Replace(ChatId chatId, Func<DialogItem, DialogItem> map)
    {
        for (var i = 0; i < _dialogs.Count; i++)
        {
            if (_dialogs[i].Id.Value == chatId.Value)
            {
                _dialogs[i] = map(_dialogs[i]);
                return;
            }
        }
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
}
