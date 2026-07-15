using System.Collections.Concurrent;
using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.UI.Fakes;

/// <summary>
/// In-memory message history for offline UI development (<c>TG_TUI_FAKE=1</c>).
/// </summary>
public sealed class FakeMessageService : IMessageService
{
    private readonly ConcurrentDictionary<long, List<ChatMessage>> _byChat = new();
    private long _nextId = 1000;

    public FakeMessageService()
    {
        // Chat 1 has a long history so load-older pagination is exercisable offline.
        var alice = new List<ChatMessage>();
        for (var i = 1; i <= 80; i++)
        {
            alice.Add(i % 3 == 0
                ? Outgoing(1, i, $"Alice thread msg #{i}", minutesAgo: 80 - i + 1, read: true)
                : Incoming(1, i, $"Alice thread msg #{i}", minutesAgo: 80 - i + 1));
        }

        Seed(new ChatId(1), alice.ToArray());

        Seed(
            new ChatId(2),
            Incoming(2, 201, "Standup notes are in the doc", minutesAgo: 180),
            Incoming(2, 202, "Bob: deploy went fine", minutesAgo: 60),
            Outgoing(2, 203, "Great, thanks", minutesAgo: 55, read: true));

        Seed(
            new ChatId(3),
            Incoming(3, 301, "Can you review the PR?", minutesAgo: 2000),
            Outgoing(3, 302, "On it", minutesAgo: 1900, read: true),
            Incoming(3, 303, "Thanks!", minutesAgo: 1440));

        Seed(
            new ChatId(4),
            Outgoing(4, 401, "shopping list", minutesAgo: 3000, read: true),
            Outgoing(4, 402, "milk\nbread\neggs", minutesAgo: 2900, read: true));

        Seed(
            new ChatId(5),
            Incoming(5, 501, "New mockups attached", minutesAgo: 45, media: true),
            Outgoing(5, 502, "Looks polished — ship it", minutesAgo: 30, read: false));
    }

    public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        ChatId chatId,
        MessageId? beforeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = GetOrCreate(chatId);
        IEnumerable<ChatMessage> query = list;
        if (beforeId is { } before)
            query = list.Where(m => m.Id.Value < before.Value);

        var page = query
            .OrderByDescending(m => m.Id.Value)
            .Take(Math.Max(1, limit))
            .OrderBy(m => m.Id.Value)
            .ToList();

        return Task.FromResult<IReadOnlyList<ChatMessage>>(page);
    }

    public Task<ChatMessage> SendTextAsync(
        ChatId chatId,
        string text,
        MessageId? replyToId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var msg = new ChatMessage
        {
            Id = new MessageId(Interlocked.Increment(ref _nextId)),
            ChatId = chatId,
            Text = text,
            IsOutgoing = true,
            SentAt = DateTimeOffset.Now,
            IsEdited = false,
            ReplyToId = replyToId,
            Media = null,
            IsRead = false,
        };

        var list = GetOrCreate(chatId);
        lock (list)
            list.Add(msg);

        return Task.FromResult(msg);
    }

    public Task EditTextAsync(
        ChatId chatId,
        MessageId messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var list = GetOrCreate(chatId);
        lock (list)
        {
            var idx = list.FindIndex(m => m.Id.Value == messageId.Value);
            if (idx < 0)
                throw new InvalidOperationException("Message not found.");

            var old = list[idx];
            if (!old.IsOutgoing)
                throw new InvalidOperationException("Only outgoing messages can be edited.");

            list[idx] = new ChatMessage
            {
                Id = old.Id,
                ChatId = old.ChatId,
                Text = text,
                IsOutgoing = old.IsOutgoing,
                SentAt = old.SentAt,
                IsEdited = true,
                ReplyToId = old.ReplyToId,
                Media = old.Media,
                IsRead = old.IsRead,
            };
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = GetOrCreate(chatId);
        lock (list)
            list.RemoveAll(m => m.Id.Value == messageId.Value);
        return Task.CompletedTask;
    }

    private List<ChatMessage> GetOrCreate(ChatId chatId) =>
        _byChat.GetOrAdd(chatId.Value, _ => []);

    private void Seed(ChatId chatId, params ChatMessage[] messages)
    {
        var list = GetOrCreate(chatId);
        lock (list)
            list.AddRange(messages);
    }

    private static ChatMessage Incoming(long chat, long id, string text, int minutesAgo, bool media = false) =>
        new()
        {
            Id = new MessageId(id),
            ChatId = new ChatId(chat),
            Text = text,
            IsOutgoing = false,
            SentAt = DateTimeOffset.Now.AddMinutes(-minutesAgo),
            IsEdited = false,
            ReplyToId = null,
            Media = media
                ? new MediaAttachment
                {
                    Kind = "photo",
                    FileName = "mockup.png",
                    MimeType = "image/png",
                    SourceChatId = new ChatId(chat),
                    SourceMessageId = new MessageId(id),
                }
                : null,
            IsRead = true,
        };

    private static ChatMessage Outgoing(long chat, long id, string text, int minutesAgo, bool read) =>
        new()
        {
            Id = new MessageId(id),
            ChatId = new ChatId(chat),
            Text = text,
            IsOutgoing = true,
            SentAt = DateTimeOffset.Now.AddMinutes(-minutesAgo),
            IsEdited = false,
            ReplyToId = null,
            Media = null,
            IsRead = read,
        };
}
