using TgTui.Core.Models;
using TL;

namespace TgTui.Telegram.Mapping;

/// <summary>
/// Encodes Telegram peer identities into a single <see cref="ChatId"/> using Bot API–style marked IDs
/// so users, basic groups, and channels never collide.
/// </summary>
public static class PeerId
{
    /// <summary>Channel marked id = -(10^12 + channel_id) → -100xxxxxxxxxx.</summary>
    public const long ChannelOffset = 1_000_000_000_000L;

    public static ChatId FromPeer(Peer peer) =>
        peer switch
        {
            PeerUser u => FromUser(u.user_id),
            PeerChat c => FromChat(c.chat_id),
            PeerChannel ch => FromChannel(ch.channel_id),
            _ => new ChatId(peer.ID)
        };

    public static ChatId FromUser(long userId) => new(userId);

    public static ChatId FromChat(long chatId) => new(-chatId);

    public static ChatId FromChannel(long channelId) => new(-(ChannelOffset + channelId));

    public static ChatId FromPeerInfo(IPeerInfo info) =>
        info switch
        {
            User => FromUser(info.ID),
            Channel => FromChannel(info.ID),
            ChatBase => FromChat(info.ID),
            _ => new ChatId(info.ID)
        };

    public static bool IsUser(ChatId id) => id.Value > 0;

    public static bool IsBasicGroup(ChatId id) => id.Value < 0 && id.Value > -ChannelOffset;

    public static bool IsChannel(ChatId id) => id.Value <= -ChannelOffset;
}
