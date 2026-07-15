using TgTui.Core.Models;
using TgTui.Core.Ports;
using TgTui.Telegram.Mapping;
using TL;
using WTelegram;

namespace TgTui.Telegram;

public sealed class WTelegramMessageService : IMessageService
{
    private readonly TelegramSession _session;
    private readonly TelegramPeerStore _peers;

    public WTelegramMessageService(TelegramSession session, TelegramPeerStore peers)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
    }

    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        ChatId chatId,
        MessageId? beforeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be positive.");

        cancellationToken.ThrowIfCancellationRequested();
        var client = _session.RequireClient();
        var peer = _peers.Require(chatId);

        var offsetId = beforeId is { } b ? checked((int)b.Value) : 0;
        var history = await client.Messages_GetHistory(peer, offset_id: offsetId, limit: limit)
            .ConfigureAwait(false);

        _peers.Merge(history);

        int? readInbox = null;
        int? readOutbox = null;
        if (_peers.TryGetReadMarkers(chatId, out var inboxMax, out var outboxMax))
        {
            readInbox = inboxMax;
            readOutbox = outboxMax;
        }

        // History is newest-first from API; present oldest→newest for UI scrolling.
        var mapped = new List<ChatMessage>();
        foreach (var msgBase in history.Messages.OrderBy(m => m.ID))
        {
            if (msgBase is not Message msg)
                continue;
            mapped.Add(TelegramMapper.MapMessage(msg, chatId, readInbox, readOutbox));
        }

        return mapped;
    }

    public async Task MarkReadAsync(ChatId chatId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = _session.RequireClient();
        var peer = _peers.Require(chatId);

        // max_id 0 = read up to the latest message (WTelegram Client.ReadHistory helper).
        await client.ReadHistory(peer).ConfigureAwait(false);

        // Incoming messages are now read; preserve existing outbox marker (peer read our messages).
        var outbox = _peers.TryGetReadMarkers(chatId, out _, out var existingOutbox)
            ? existingOutbox
            : 0;
        _peers.SetReadMarkers(chatId, int.MaxValue, outbox);
    }

    public async Task<ChatMessage> SendTextAsync(
        ChatId chatId,
        string text,
        MessageId? replyToId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        var client = _session.RequireClient();
        var peer = _peers.Require(chatId);
        var reply = replyToId is { } r ? checked((int)r.Value) : 0;

        var sent = await client.SendMessageAsync(peer, text, reply_to_msg_id: reply)
            .ConfigureAwait(false);

        return TelegramMapper.MapMessage(sent, chatId);
    }

    public async Task EditTextAsync(
        ChatId chatId,
        MessageId messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        var client = _session.RequireClient();
        var peer = _peers.Require(chatId);

        await client.Messages_EditMessage(peer, checked((int)messageId.Value), message: text)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        ChatId chatId,
        MessageId messageId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = _session.RequireClient();
        var peer = _peers.Require(chatId);

        await client.DeleteMessages(peer, new[] { checked((int)messageId.Value) })
            .ConfigureAwait(false);
    }
}
