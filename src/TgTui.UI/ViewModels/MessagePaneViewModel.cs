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
    private string _searchQuery = "";
    private readonly List<int> _searchMatches = [];
    private int _searchMatchCursor = -1;

    public MessagePaneViewModel(IMessageService messages, IMediaService media, IUpdateHub? hub = null)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _hub = hub;
        if (_hub is not null)
            _hub.MessagesChanged += OnMessagesChanged;
    }

    public event Action? Changed;

    /// <summary>Raised after a successful open that marked the chat as read (chat id).</summary>
    public event Action<ChatId>? ChatMarkedRead;

    public ChatId? ChatId { get; private set; }

    public string ChatTitle { get; private set; } = "";

    public IReadOnlyList<ChatMessage> Messages => _items;

    public bool HasMoreHistory => _hasMoreHistory;

    public bool IsLoadingOlder => _loadingOlder;

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(_searchQuery);

    public int SearchMatchCount => _searchMatches.Count;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            var next = value ?? "";
            if (string.Equals(_searchQuery, next, StringComparison.Ordinal))
                return;
            _searchQuery = next;
            RecomputeSearchMatches(selectFirst: true);
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

    /// <summary>One-line snippet of a message for reply quotes (null when not in the open chat).</summary>
    public string? GetReplySnippet(MessageId replyToId)
    {
        var msg = _items.FirstOrDefault(m => m.Id.Value == replyToId.Value);
        return msg is null ? null : FormatReplySnippet(msg);
    }

    /// <summary>Builds multi-line detail text for the expanded message dialog.</summary>
    public async Task<string> BuildDetailContentAsync(
        ChatMessage message,
        int maxCellWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sb = new System.Text.StringBuilder();
        var time = message.SentAt.ToLocalTime().ToString("g");
        var direction = message.IsOutgoing ? "Outgoing" : "Incoming";
        var edited = message.IsEdited ? " · edited" : "";
        var pending = message.Id.Value < 0 ? " · sending…" : "";
        sb.AppendLine($"{direction} · {time}{edited}{pending}");

        if (message.ReplyToId is { } replyId)
        {
            var quote = GetReplySnippet(replyId);
            sb.AppendLine(quote is not null ? $"↩ {quote}" : $"↩ message #{replyId.Value}");
        }

        sb.AppendLine(new string('─', Math.Min(48, Math.Max(16, maxCellWidth))));

        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            sb.AppendLine(message.Text);
            sb.AppendLine();
        }

        if (message.Media is { } media)
        {
            if (IsInlinePreviewable(media))
            {
                var preview = await LoadExpandedMediaPreviewAsync(media, maxCellWidth, cancellationToken)
                    .ConfigureAwait(false);
                sb.AppendLine(string.IsNullOrWhiteSpace(preview)
                    ? MediaUnavailablePlaceholder
                    : preview);
            }
            else
            {
                var label = string.IsNullOrWhiteSpace(media.FileName)
                    ? $"[{media.Kind}]"
                    : $"[{media.Kind}] {media.FileName}";
                sb.AppendLine(label);
            }

            sb.AppendLine();
            sb.AppendLine("o · open attachment externally");
        }

        sb.Append("Esc · close");
        return sb.ToString().TrimEnd();
    }

    public string? GetMediaPreview(MessageId messageId) =>
        _mediaPreviews.TryGetValue(messageId.Value, out var p) ? p : null;

    public async Task OpenChatAsync(DialogItem dialog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        ChatId = dialog.Id;
        ChatTitle = dialog.Title;
        ReplyToId = null;
        ClearSearch();
        _hasMoreHistory = true;
        await ReloadAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _messages.MarkReadAsync(dialog.Id, cancellationToken).ConfigureAwait(false);
            ChatMarkedRead?.Invoke(dialog.Id);
        }
        catch
        {
            // Opening the chat still succeeds if mark-read fails (offline / permission).
        }
    }

    /// <param name="preserveScrollPosition">
    /// When <c>true</c>, keep the selected message after reload unless the user was already at the latest.
    /// </param>
    public async Task ReloadAsync(
        CancellationToken cancellationToken = default,
        bool preserveScrollPosition = false)
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

        var anchorId = preserveScrollPosition ? Selected?.Id : null;
        var wasAtLatest = preserveScrollPosition && IsAtLatest();

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

        if (preserveScrollPosition && !wasAtLatest && anchorId is { } id)
        {
            var idx = _items.FindIndex(m => m.Id.Value == id.Value);
            _selectedIndex = idx >= 0 ? idx : ClampIndex(_selectedIndex);
        }
        else
        {
            JumpToLatest(raise: false);
        }

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

    public void ClearSearch()
    {
        if (string.IsNullOrEmpty(_searchQuery) && _searchMatches.Count == 0)
            return;

        _searchQuery = "";
        _searchMatches.Clear();
        _searchMatchCursor = -1;
        RaiseChanged();
    }

    /// <summary>Moves to the next (<paramref name="direction"/> = 1) or previous (-1) in-chat search match.</summary>
    public bool MoveSearchMatch(int direction)
    {
        if (_searchMatches.Count == 0 || direction == 0)
            return false;

        if (_searchMatchCursor < 0)
            _searchMatchCursor = direction > 0 ? 0 : _searchMatches.Count - 1;
        else
            _searchMatchCursor = (_searchMatchCursor + direction + _searchMatches.Count) % _searchMatches.Count;

        _selectedIndex = _searchMatches[_searchMatchCursor];
        RaiseChanged();
        _ = EnsureMediaPreviewAsync(Selected);
        return true;
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

    public string FormatRow(ChatMessage message, int maxWidth, int rowIndex = -1)
    {
        var time = message.SentAt.ToLocalTime().ToString("HH:mm");
        var pending = message.Id.Value < 0;
        var receipt = message.IsOutgoing
            ? (pending ? " …" : (message.IsRead ? " ✓✓" : " ✓"))
            : "";
        var edited = message.IsEdited ? " (edited)" : "";
        var reply = message.ReplyToId is { } replyId
            ? FormatReplyPrefix(replyId)
            : "";
        var body = string.IsNullOrWhiteSpace(message.Text)
            ? (message.Media is not null ? $"[{message.Media.Kind}]" : "")
            : message.Text.Replace('\n', ' ');

        var preview = GetMediaPreview(message.Id);
        if (!string.IsNullOrEmpty(preview))
            body = string.IsNullOrEmpty(body) ? preview : $"{body} · {OneLine(preview)}";

        var searchMark = "";
        if (IsSearchActive && rowIndex >= 0 && _searchMatches.Contains(rowIndex))
            searchMark = _searchMatchCursor >= 0 && _searchMatches[_searchMatchCursor] == rowIndex ? "»" : "·";

        var align = message.IsOutgoing ? "→" : "←";
        var line = $"{searchMark}{align} {time}{receipt}{edited} {reply}{body}";
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

        switch (e.Kind)
        {
            case MessageChangeKind.Refresh:
                _ = ReloadQuietAsync();
                break;
            case MessageChangeKind.Added when e.Message is not null:
                ApplyMessageAdded(e.Message);
                break;
            case MessageChangeKind.Edited when e.Message is not null:
                ApplyMessageEdited(e.Message);
                break;
            case MessageChangeKind.Deleted:
                ApplyMessagesDeleted(e.DeletedIds);
                break;
            case MessageChangeKind.ReadStateChanged:
                ApplyReadStateChanged(e.ReadInboxMaxId, e.ReadOutboxMaxId);
                break;
            default:
                _ = ReloadQuietAsync();
                break;
        }
    }

    private void ApplyMessageAdded(ChatMessage message)
    {
        var wasAtLatest = IsAtLatest();
        var anchorId = wasAtLatest ? null : Selected?.Id;

        UpsertById(message);
        _items.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));

        if (wasAtLatest)
            JumpToLatest(raise: false);
        else if (anchorId is { } aid)
        {
            var idx = _items.FindIndex(m => m.Id.Value == aid.Value);
            _selectedIndex = idx >= 0 ? idx : ClampIndex(_selectedIndex);
        }

        RecomputeSearchMatches(selectFirst: false);
        RaiseChanged();
        if (wasAtLatest)
            _ = EnsureMediaPreviewAsync(Selected);
    }

    private void ApplyMessageEdited(ChatMessage message)
    {
        UpsertById(message);
        _mediaPreviews.Remove(message.Id.Value);
        RecomputeSearchMatches(selectFirst: false);
        RaiseChanged();
        _ = EnsureMediaPreviewAsync(Selected);
    }

    private void ApplyMessagesDeleted(IReadOnlyList<MessageId>? deletedIds)
    {
        if (deletedIds is null || deletedIds.Count == 0)
            return;

        var ids = new HashSet<long>(deletedIds.Select(static id => id.Value));
        _items.RemoveAll(m => ids.Contains(m.Id.Value));
        foreach (var id in ids)
            _mediaPreviews.Remove(id);

        _selectedIndex = ClampIndex(_selectedIndex);
        RecomputeSearchMatches(selectFirst: false);
        RaiseChanged();
    }

    private void ApplyReadStateChanged(int? readInboxMaxId, int? readOutboxMaxId)
    {
        if (readInboxMaxId is null && readOutboxMaxId is null)
            return;

        var changed = false;
        for (var i = 0; i < _items.Count; i++)
        {
            var msg = _items[i];
            var isRead = msg.IsOutgoing
                ? readOutboxMaxId is int outMax && msg.Id.Value <= outMax
                : readInboxMaxId is int inMax && msg.Id.Value <= inMax;
            if (isRead == msg.IsRead)
                continue;

            _items[i] = new ChatMessage
            {
                Id = msg.Id,
                ChatId = msg.ChatId,
                Text = msg.Text,
                IsOutgoing = msg.IsOutgoing,
                SentAt = msg.SentAt,
                IsEdited = msg.IsEdited,
                ReplyToId = msg.ReplyToId,
                Media = msg.Media,
                IsRead = isRead,
            };
            changed = true;
        }

        if (changed)
            RaiseChanged();
    }

    private void RecomputeSearchMatches(bool selectFirst)
    {
        _searchMatches.Clear();
        _searchMatchCursor = -1;
        if (string.IsNullOrWhiteSpace(_searchQuery))
            return;

        var query = _searchQuery.Trim();
        for (var i = 0; i < _items.Count; i++)
        {
            if (MessageMatchesSearch(_items[i], query))
                _searchMatches.Add(i);
        }

        if (!selectFirst || _searchMatches.Count == 0)
            return;

        _searchMatchCursor = 0;
        _selectedIndex = _searchMatches[0];
    }

    private static bool MessageMatchesSearch(ChatMessage message, string query)
    {
        if (!string.IsNullOrEmpty(message.Text)
            && message.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(message.Media?.FileName)
               && message.Media.FileName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ReloadQuietAsync()
    {
        try
        {
            await ReloadAsync(preserveScrollPosition: true).ConfigureAwait(false);
        }
        catch
        {
            // ignore hub-driven failures
        }
    }

    private bool IsAtLatest() =>
        _items.Count == 0 || _selectedIndex >= _items.Count - 1;

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

    private async Task<string?> LoadExpandedMediaPreviewAsync(
        MediaAttachment media,
        int maxCellWidth,
        CancellationToken cancellationToken)
    {
        var local = await ResolveLocalMediaPathAsync(media, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(local))
            return null;

        try
        {
            return _media.RenderPreview(local, maxCellWidth);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ResolveLocalMediaPathAsync(
        MediaAttachment media,
        CancellationToken cancellationToken)
    {
        var local = media.LocalPath;
        if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
            local = await _media.EnsureLocalAsync(media, cancellationToken).ConfigureAwait(false);
        return local;
    }

    private string FormatReplyPrefix(MessageId replyToId)
    {
        var snippet = GetReplySnippet(replyToId);
        if (string.IsNullOrWhiteSpace(snippet))
            return $"↩ #{replyToId.Value} ";

        var oneLine = OneLine(snippet);
        if (oneLine.Length > 28)
            oneLine = oneLine[..27] + "…";
        return $"↩ {oneLine} ";
    }

    private static string FormatReplySnippet(ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Text))
            return message.Text.Replace('\n', ' ').Trim();

        if (message.Media is { } media)
        {
            if (!string.IsNullOrWhiteSpace(media.FileName))
                return $"[{media.Kind}] {media.FileName}";
            return $"[{media.Kind}]";
        }

        return "(message)";
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
