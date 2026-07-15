using TgTui.Core.Events;
using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.UI.ViewModels;

/// <summary>
/// Message history for the open chat. UI marshals <see cref="Changed"/> to the main thread.
/// </summary>
public sealed class MessagePaneViewModel : IDisposable
{
    private const int DefaultHistoryLimit = 50;

    private readonly IMessageService _messages;
    private readonly IMediaService _media;
    private readonly IUpdateHub? _hub;
    private readonly List<ChatMessage> _items = [];
    private readonly Dictionary<long, string> _mediaPreviews = new();
    private int _selectedIndex;
    private bool _disposed;
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
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (ChatId is not { } chatId)
        {
            _items.Clear();
            _mediaPreviews.Clear();
            _selectedIndex = 0;
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

        _items.Clear();
        _items.AddRange(history);
        _mediaPreviews.Clear();
        JumpToLatest(raise: false);
        RaiseChanged();
        _ = EnsureMediaPreviewAsync(Selected);
    }

    public void MoveSelection(int delta)
    {
        if (_items.Count == 0)
            return;
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
        var idx = _items.FindIndex(m => m.Id.Value == msg.Id.Value);
        if (idx >= 0)
        {
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
    }

    public async Task OpenSelectedMediaExternallyAsync(CancellationToken cancellationToken = default)
    {
        if (Selected?.Media is not { } media)
            return;

        var local = media.LocalPath;
        if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
            local = await _media.EnsureLocalAsync(media, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(local))
            return;

        await _media.OpenExternallyAsync(local, cancellationToken).ConfigureAwait(false);
    }

    public string FormatRow(ChatMessage message, int maxWidth)
    {
        var time = message.SentAt.ToLocalTime().ToString("HH:mm");
        var receipt = message.IsOutgoing
            ? (message.IsRead ? " ✓✓" : " ✓")
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
        if (_mediaPreviews.ContainsKey(message.Id.Value))
            return;

        try
        {
            var local = media.LocalPath;
            if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
                local = await _media.EnsureLocalAsync(media).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(local))
            {
                _mediaPreviews[message.Id.Value] = "🖼 (loading failed — o to open)";
            }
            else
            {
                // Keep list rows compact; full inline graphics is Task 12.
                _mediaPreviews[message.Id.Value] = OneLine(_media.RenderPreview(local, maxCellWidth: 40));
            }

            RaiseChanged();
        }
        catch
        {
            _mediaPreviews[message.Id.Value] = "🖼";
            RaiseChanged();
        }
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
