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

    public void Put(ChatId chatId, InputPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        _peers[chatId.Value] = peer;
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
