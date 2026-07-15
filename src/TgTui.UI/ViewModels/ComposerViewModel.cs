using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.UI.ViewModels;

/// <summary>
/// Composer text, reply/edit targets, and draft persistence for the open chat.
/// </summary>
public sealed class ComposerViewModel
{
    private readonly IMessageService _messages;
    private readonly IDraftStore _drafts;
    private ChatId? _chatId;
    private string _text = "";
    private MessageId? _replyToId;
    private MessageId? _editMessageId;

    public ComposerViewModel(IMessageService messages, IDraftStore drafts)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
    }

    public event Action? Changed;

    public ChatId? ChatId => _chatId;

    public string Text
    {
        get => _text;
        set
        {
            var next = value ?? "";
            if (string.Equals(_text, next, StringComparison.Ordinal))
                return;
            _text = next;
            RaiseChanged();
        }
    }

    public MessageId? ReplyToId
    {
        get => _replyToId;
        set
        {
            if (_replyToId == value)
                return;
            _replyToId = value;
            if (value is not null)
                _editMessageId = null;
            RaiseChanged();
        }
    }

    public MessageId? EditMessageId => _editMessageId;

    public string StatusHint
    {
        get
        {
            if (_chatId is null)
                return "No chat selected";
            if (_editMessageId is { } e)
                return $"Editing #{e.Value} · Enter apply · Esc cancel · Shift+Enter newline";
            if (_replyToId is { } r)
                return $"Reply to #{r.Value} · Enter send · Esc cancel · Shift+Enter newline";
            return "Enter send · Esc leave · Shift+Enter newline · draft auto-saved";
        }
    }

    /// <summary>
    /// Switch open chat: persist previous draft, load next draft, clear reply/edit.
    /// </summary>
    public async Task BindChatAsync(ChatId? chatId, MessageId? replyToId = null, CancellationToken cancellationToken = default)
    {
        if (_chatId is { } previous && previous.Value != chatId?.Value)
            await PersistDraftAsync(cancellationToken).ConfigureAwait(false);

        _chatId = chatId;
        _replyToId = replyToId;
        _editMessageId = null;
        _text = chatId is { } id ? (_drafts.GetDraft(id) ?? "") : "";
        RaiseChanged();
    }

    public void SetReply(MessageId? replyToId)
    {
        _editMessageId = null;
        ReplyToId = replyToId;
    }

    public void BeginEdit(MessageId messageId, string text)
    {
        _replyToId = null;
        _editMessageId = messageId;
        _text = text ?? "";
        RaiseChanged();
    }

    /// <summary>
    /// Cancels reply or edit. Returns <c>true</c> if something was canceled.
    /// When canceling edit, restores composer text from the saved draft so a later
    /// <see cref="PersistDraftAsync"/> does not overwrite the real draft with the edit body.
    /// </summary>
    public bool CancelReplyOrEdit()
    {
        if (_editMessageId is not null)
        {
            _editMessageId = null;
            _text = _chatId is { } id ? (_drafts.GetDraft(id) ?? "") : "";
            RaiseChanged();
            return true;
        }

        if (_replyToId is not null)
        {
            _replyToId = null;
            RaiseChanged();
            return true;
        }

        return false;
    }

    public void CancelReply() => CancelReplyOrEdit();

    public async Task PersistDraftAsync(CancellationToken cancellationToken = default)
    {
        if (_chatId is not { } id)
            return;

        // Do not clobber drafts while editing an existing message.
        if (_editMessageId is not null)
            return;

        _drafts.SetDraft(id, string.IsNullOrWhiteSpace(_text) ? null : _text);
        await _drafts.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a new message or applies an in-progress edit. Returns the sent message when created.
    /// </summary>
    public async Task<ChatMessage?> SubmitAsync(CancellationToken cancellationToken = default)
    {
        if (_chatId is not { } id)
            return null;

        var body = _text.Trim();
        if (body.Length == 0)
            return null;

        if (_editMessageId is { } editId)
        {
            await _messages.EditTextAsync(id, editId, body, cancellationToken).ConfigureAwait(false);
            _editMessageId = null;
            _text = _drafts.GetDraft(id) ?? "";
            RaiseChanged();
            return null;
        }

        var sent = await _messages
            .SendTextAsync(id, body, _replyToId, cancellationToken)
            .ConfigureAwait(false);

        _text = "";
        _replyToId = null;
        _drafts.SetDraft(id, null);
        await _drafts.SaveAsync(cancellationToken).ConfigureAwait(false);
        RaiseChanged();
        return sent;
    }

    private void RaiseChanged() => Changed?.Invoke();
}
