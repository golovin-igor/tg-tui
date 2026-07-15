using TgTui.Core.Events;
using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.UI.ViewModels;

/// <summary>
/// Message history for the open chat. UI marshals <see cref="Changed"/> to the main thread.
/// Supports optimistic outgoing inserts and paginated older history.
/// </summary>
public sealed class MessagePaneViewModel : IDisposable
{
    private const int DefaultHistoryLimit = 50;

    /// <summary>Shown while <see cref="IMediaService.EnsureLocalAsync"/> is in flight.</summary>
    public const string MediaLoadingPlaceholder = "🖼 loading…";

    /// <summary>Shown when download/render fails; matches half-block unavailable text.</summary>
    public const string MediaUnavailablePlaceholder = "🖼 image unavailable";

    private readonly IMessageService _messages;
    private readonly IMediaService _media;
    private readonly IUpdateHub? _hub;
    private readonly List<ChatMessage> _items = [];
    private readonly Dictionary<long, string> _mediaPreviews = new();
    private readonly HashSet<long> _mediaPreviewInFlight = new();
    private int _selectedIndex;
    private bool _disposed;
    private bool _hasMoreHistory = true;
    private bool _loadingOlder;
    private CancellationTokenSource? _loadCts;

    public MessagePaneViewModel(IMessageService messages, IMediaService media, IUpdateHub? hub = null)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _hub = hub;
        if (_hub is not null)
            _hub.MessagesChanged += OnMessagesChanged;
    }

    public event Action? Changed;

    public ChatId? ChatId { get; private set; }

    public string ChatTitle { get; private set; } = "";

    public IReadOnlyList<ChatMessage> Messages => _items;

    public bool HasMoreHistory => _hasMoreHistory;

    public bool IsLoadingOlder => _loadingOlder;

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
            _ = EnsureMediaPreviewAsync(Selected);
            if (next == 0)
                _ = LoadOlderAsync();
        }
    }

    public ChatMessage? Selected =>
        _items.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? _items[_selectedIndex]
            : null;

    /// <summary>Reply target set by message-pane <c>r</c>; composer reads/clears this.</summary>
    public MessageId? ReplyToId { get; set; }

    public string? GetMediaPreview(MessageId messageId) =>
        _mediaPreviews.TryGetValue(messageId.Value, out var p) ? p : null;

    public async Task OpenChatAsync(DialogItem dialog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        ChatId = dialog.Id;
        ChatTitle = dialog.Title;
        ReplyToId = null;
        _hasMoreHistory = true;
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (ChatId is not { } chatId)
        {
            _items.Clear();
            _mediaPreviews.Clear();
            _mediaPreviewInFlight.Clear();
            _selectedIndex = 0;
            _hasMoreHistory = false;
            RaiseChanged();
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _loadCts.Token;

        var history = await _messages
            .GetHistoryAsync(chatId, beforeId: null, DefaultHistoryLimit, ct)
            .ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        // Preserve in-flight optimistic (negative id) rows that the server has not confirmed yet.
        var pending = _items.Where(static m => m.Id.Value < 0).ToList();

        _items.Clear();
        _items.AddRange(history);

        foreach (var opt in pending)
        {
            if (!_items.Any(m => m.Id.Value == opt.Id.Value
                                 || (m.IsOutgoing
                                     && string.Equals(m.Text, opt.Text, StringComparison.Ordinal)
                                     && m.SentAt >= opt.SentAt.AddSeconds(-5))))
            {
                _items.Add(opt);
            }
        }

        _mediaPreviews.Clear();
        _mediaPreviewInFlight.Clear();
        _hasMoreHistory = history.Count >= DefaultHistoryLimit;
        JumpToLatest(raise: false);
        RaiseChanged();
        _ = EnsureMediaPreviewAsync(Selected);
    }

    /// <summary>
    /// Loads a page of messages older than the earliest real (positive-id) message.
    /// Called when the selection reaches the top of the list.
    /// </summary>
    public async Task LoadOlderAsync(CancellationToken cancellationToken = default)
    {
        if (_loadingOlder || !_hasMoreHistory || ChatId is not { } chatId || _items.Count == 0)
            return;

        var oldestReal = FindOldestRealMessageId();
        if (oldestReal is null)
            return;

        _loadingOlder = true;
        try
        {
            var older = await _messages
                .GetHistoryAsync(chatId, oldestReal, DefaultHistoryLimit, cancellationToken)
                .ConfigureAwait(false);

            if (older.Count == 0)
            {
                _hasMoreHistory = false;
                return;
            }

            var existing = new HashSet<long>(_items.Select(static m => m.Id.Value));
            var toPrepend = older.Where(m => !existing.Contains(m.Id.Value)).ToList();
            if (toPrepend.Count == 0)
            {
                if (older.Count < DefaultHistoryLimit)
                    _hasMoreHistory = false;
                return;
            }

            var selectedId = Selected?.Id;
            _items.InsertRange(0, toPrepend);

            if (selectedId is { } sid)
            {
                var idx = _items.FindIndex(m => m.Id.Value == sid.Value);
                _selectedIndex = idx >= 0 ? idx : ClampIndex(_selectedIndex + toPrepend.Count);
            }
            else
            {
                _selectedIndex = ClampIndex(_selectedIndex + toPrepend.Count);
            }

            if (older.Count < DefaultHistoryLimit)
                _hasMoreHistory = false;

            RaiseChanged();
        }
        catch
        {
            // Keep existing list on page load failure.
        }
        finally
        {
            _loadingOlder = false;
        }
    }

    /// <summary>Inserts a temporary outgoing message at the bottom (optimistic send).</summary>
    public void PresentOptimistic(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (ChatId is not { } id || message.ChatId.Value != id.Value)
            return;

        UpsertById(message);
        JumpToLatest(raise: false);
        RaiseChanged();
    }

    /// <summary>Replaces an optimistic row with the server-confirmed message.</summary>
    public void ConfirmOptimistic(MessageId optimisticId, ChatMessage confirmed)
    {
        ArgumentNullException.ThrowIfNull(confirmed);

        var optIdx = _items.FindIndex(m => m.Id.Value == optimisticId.Value);
        var confirmedIdx = _items.FindIndex(m => m.Id.Value == confirmed.Id.Value);

        if (optIdx >= 0 && confirmedIdx >= 0 && optIdx != confirmedIdx)
        {
            _items.RemoveAt(Math.Max(optIdx, confirmedIdx));
            var keep = Math.Min(optIdx, confirmedIdx);
            _items[keep] = confirmed;
        }
        else if (optIdx >= 0)
        {
            _items[optIdx] = confirmed;
        }
        else if (confirmedIdx >= 0)
        {
            _items[confirmedIdx] = confirmed;
        }
        else
        {
            _items.Add(confirmed);
        }

        _mediaPreviews.Remove(optimisticId.Value);
        JumpToLatest(raise: false);
        RaiseChanged();
    }

    /// <summary>Drops a failed optimistic send.</summary>
    public void CancelOptimistic(MessageId optimisticId)
    {
        var removed = _items.RemoveAll(m => m.Id.Value == optimisticId.Value);
        if (removed == 0)
            return;
        _mediaPreviews.Remove(optimisticId.Value);
        _selectedIndex = ClampIndex(_selectedIndex);
        RaiseChanged();
    }

    /// <summary>Applies a local edit to a message already in the list.</summary>
    public void ApplyLocalEdit(MessageId messageId, string newText)
    {
        var idx = _items.FindIndex(m => m.Id.Value == messageId.Value);
        if (idx < 0)
            return;

        var msg = _items[idx];
        _items[idx] = new ChatMessage
        {
            Id = msg.Id,
            ChatId = msg.ChatId,
            Text = newText,
            IsOutgoing = msg.IsOutgoing,
            SentAt = msg.SentAt,
            IsEdited = true,
            ReplyToId = msg.ReplyToId,
            Media = msg.Media,
            IsRead = msg.IsRead,
        };
        RaiseChanged();
    }

    public void MoveSelection(int delta)
    {
        if (_items.Count == 0)
            return;

        if (delta < 0 && _selectedIndex <= 0)
        {
            _ = LoadOlderAsync();
            return;
        }

        SelectedIndex = _selectedIndex + delta;
    }

    public void JumpToLatest(bool raise = true)
    {
        _selectedIndex = _items.Count == 0 ? 0 : _items.Count - 1;
        if (raise)
            RaiseChanged();
    }

    public void BeginReply()
    {
        var selected = Selected;
        if (selected is null)
            return;
        ReplyToId = selected.Id;
        RaiseChanged();
    }

    public void CancelReply()
    {
        if (ReplyToId is null)
            return;
        ReplyToId = null;
        RaiseChanged();
    }

    public async Task DeleteSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (ChatId is not { } chatId || Selected is not { } msg)
            return;

        await _messages.DeleteAsync(chatId, msg.Id, cancellationToken).ConfigureAwait(false);
        _items.RemoveAll(m => m.Id.Value == msg.Id.Value);
        _mediaPreviews.Remove(msg.Id.Value);
        _selectedIndex = ClampIndex(_selectedIndex);
        RaiseChanged();
    }

    public async Task EditSelectedAsync(string newText, CancellationToken cancellationToken = default)
    {
        if (ChatId is not { } chatId || Selected is not { } msg || !msg.IsOutgoing)
            return;

        await _messages.EditTextAsync(chatId, msg.Id, newText, cancellationToken).ConfigureAwait(false);
        ApplyLocalEdit(msg.Id, newText);
    }

    public async Task OpenSelectedMediaExternallyAsync(CancellationToken cancellationToken = default)
    {
        if (Selected?.Media is not { } media)
            return;

        try
        {
            var local = media.LocalPath;
            if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
                local = await _media.EnsureLocalAsync(media, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(local))
                return;

            await _media.OpenExternallyAsync(local, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // External open / download must never break message navigation.
        }
    }

    public string FormatRow(ChatMessage message, int maxWidth)
    {
        var time = message.SentAt.ToLocalTime().ToString("HH:mm");
        var pending = message.Id.Value < 0;
        var receipt = message.IsOutgoing
            ? (pending ? " …" : (message.IsRead ? " ✓✓" : " ✓"))
            : "";
        var edited = message.IsEdited ? " (edited)" : "";
        var reply = message.ReplyToId is { } r ? $"↩ {r.Value} " : "";
        var body = string.IsNullOrWhiteSpace(message.Text)
            ? (message.Media is not null ? $"[{message.Media.Kind}]" : "")
            : message.Text.Replace('\n', ' ');

        var preview = GetMediaPreview(message.Id);
        if (!string.IsNullOrEmpty(preview))
            body = string.IsNullOrEmpty(body) ? preview : $"{body} · {OneLine(preview)}";

        var align = message.IsOutgoing ? "→" : "←";
        var line = $"{align} {time}{receipt}{edited} {reply}{body}";
        if (maxWidth > 8 && line.Length > maxWidth)
            return line[..(maxWidth - 1)] + "…";
        return line;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        if (_hub is not null)
            _hub.MessagesChanged -= OnMessagesChanged;
    }

    private MessageId? FindOldestRealMessageId()
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Id.Value > 0)
                return _items[i].Id;
        }

        return null;
    }

    private void UpsertById(ChatMessage message)
    {
        var idx = _items.FindIndex(m => m.Id.Value == message.Id.Value);
        if (idx >= 0)
            _items[idx] = message;
        else
            _items.Add(message);
    }

    private void OnMessagesChanged(MessagesChanged e)
    {
        if (ChatId is not { } id || e.ChatId.Value != id.Value)
            return;
        _ = ReloadQuietAsync();
    }

    private async Task ReloadQuietAsync()
    {
        try
        {
            await ReloadAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore hub-driven failures
        }
    }

    private async Task EnsureMediaPreviewAsync(ChatMessage? message)
    {
        if (message?.Media is not { } media)
            return;

        var messageId = message.Id.Value;

        // Already resolved (success or failure) — do not re-download on every reselect.
        if (_mediaPreviews.TryGetValue(messageId, out var existing)
            && existing != MediaLoadingPlaceholder)
            return;

        // Another load for this id is already running.
        if (!_mediaPreviewInFlight.Add(messageId))
            return;

        _mediaPreviews[messageId] = MediaLoadingPlaceholder;
        RaiseChanged();

        try
        {
            // Non-image attachments: label only; open externally still works via `o`.
            if (!IsInlinePreviewable(media))
            {
                var label = string.IsNullOrWhiteSpace(media.FileName)
                    ? $"[{media.Kind}] · o to open"
                    : $"[{media.Kind}] {media.FileName} · o to open";
                _mediaPreviews[messageId] = OneLine(label);
                RaiseChanged();
                return;
            }

            var local = media.LocalPath;
            if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
                local = await _media.EnsureLocalAsync(media).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(local))
            {
                _mediaPreviews[messageId] = MediaUnavailablePlaceholder;
            }
            else
            {
                // ListView rows are single-line; protocol sequences and first half-block row fit.
                // Multi-line half-block is truncated to the first visual line.
                string rendered;
                try
                {
                    rendered = _media.RenderPreview(local, maxCellWidth: 40);
                }
                catch
                {
                    rendered = MediaUnavailablePlaceholder;
                }

                _mediaPreviews[messageId] = string.IsNullOrWhiteSpace(rendered)
                    ? MediaUnavailablePlaceholder
                    : OneLine(rendered);
            }

            RaiseChanged();
        }
        catch
        {
            // Failures must never block j/k scroll or selection.
            _mediaPreviews[messageId] = MediaUnavailablePlaceholder;
            RaiseChanged();
        }
        finally
        {
            _mediaPreviewInFlight.Remove(messageId);
        }
    }

    private static bool IsInlinePreviewable(MediaAttachment media)
    {
        if (string.Equals(media.Kind, "photo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(media.Kind, "sticker", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(media.MimeType)
            && media.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        // Generic documents: only try decode when the file looks like a common image extension.
        if (!string.IsNullOrWhiteSpace(media.FileName))
        {
            var ext = Path.GetExtension(media.FileName);
            if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp")
                return true;
        }

        return false;
    }

    private int ClampIndex(int index)
    {
        if (_items.Count == 0)
            return 0;
        if (index < 0)
            return 0;
        if (index >= _items.Count)
            return _items.Count - 1;
        return index;
    }

    private void RaiseChanged() => Changed?.Invoke();

    private static string OneLine(string text)
    {
        var first = text.Split('\n', 2)[0].Trim();
        return first.Length > 60 ? first[..59] + "…" : first;
    }
}
