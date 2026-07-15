using System.Collections.Concurrent;
using TgTui.Core.Models;
using TgTui.Telegram.Mapping;
using TL;

namespace TgTui.Telegram;

/// <summary>
/// In-memory access_hash-aware peer cache keyed by domain <see cref="ChatId"/>.
/// Populated from dialogs / history responses.
/// </summary>
public sealed class TelegramPeerStore
{
    private readonly ConcurrentDictionary<long, InputPeer> _peers = new();
    private readonly ConcurrentDictionary<long, (int InboxMaxId, int OutboxMaxId)> _readMarkers = new();

    public void Merge(IDictionary<long, User>? users, IDictionary<long, ChatBase>? chats)
    {
        if (users is not null)
        {
            foreach (var (_, user) in users)
            {
                if (user is not User u || !u.IsActive)
                    continue;
                try
                {
                    InputPeer peer = u;
                    _peers[PeerId.FromUser(u.id).Value] = peer;
                }
                catch
                {
                    // Missing access_hash or empty user — skip.
                }
            }
        }

        if (chats is not null)
        {
            foreach (var (_, chat) in chats)
            {
                if (!chat.IsActive)
                    continue;
                try
                {
                    InputPeer peer = chat;
                    var key = chat is Channel
                        ? PeerId.FromChannel(chat.ID).Value
                        : PeerId.FromChat(chat.ID).Value;
                    _peers[key] = peer;
                }
                catch
                {
                    // Forbidden / missing access_hash — skip.
                }
            }
        }
    }

    public void Merge(Messages_Dialogs dialogs)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        Merge(dialogs.users, dialogs.chats);
    }

    /// <summary>
    /// Merges users/chats from history / getMessages responses.
    /// Handles <see cref="Messages_Messages"/>, <see cref="Messages_MessagesSlice"/>
    /// (inherits messages), and <see cref="Messages_ChannelMessages"/>.
    /// </summary>
    public void Merge(Messages_MessagesBase messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        switch (messages)
        {
            case Messages_ChannelMessages channel:
                Merge(channel.users, channel.chats);
                break;
            // Explicit slice arm documents API constructor; Slice subclasses Messages_Messages.
            case Messages_MessagesSlice slice:
                Merge(slice.users, slice.chats);
                break;
            case Messages_Messages regular:
                Merge(regular.users, regular.chats);
                break;
        }
    }

    public void Put(ChatId chatId, InputPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        _peers[chatId.Value] = peer;
    }

    public void SetReadMarkers(ChatId chatId, int readInboxMaxId, int readOutboxMaxId) =>
        _readMarkers[chatId.Value] = (readInboxMaxId, readOutboxMaxId);

    public bool TryGetReadMarkers(ChatId chatId, out int readInboxMaxId, out int readOutboxMaxId)
    {
        if (_readMarkers.TryGetValue(chatId.Value, out var markers))
        {
            readInboxMaxId = markers.InboxMaxId;
            readOutboxMaxId = markers.OutboxMaxId;
            return true;
        }

        readInboxMaxId = 0;
        readOutboxMaxId = 0;
        return false;
    }

    public bool TryGet(ChatId chatId, out InputPeer peer) =>
        _peers.TryGetValue(chatId.Value, out peer!);

    public InputPeer Require(ChatId chatId)
    {
        if (TryGet(chatId, out var peer))
            return peer;
        throw new InvalidOperationException(
            $"Unknown peer for chat {chatId.Value}. Load dialogs first or resolve the peer.");
    }
}
