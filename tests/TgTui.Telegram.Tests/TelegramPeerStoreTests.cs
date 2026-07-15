using FluentAssertions;
using TgTui.Telegram;
using TgTui.Telegram.Mapping;
using TL;

namespace TgTui.Telegram.Tests;

public class TelegramPeerStoreTests
{
    [Fact]
    public void Merge_Messages_MessagesSlice_merges_users_and_chats()
    {
        var store = new TelegramPeerStore();

        var user = new User
        {
            id = 11,
            flags = User.Flags.has_access_hash,
            access_hash = 1001
        };
        var chat = new Chat { id = 22, title = "group" };
        var channel = new Channel
        {
            id = 33,
            title = "chan",
            flags = Channel.Flags.has_access_hash,
            access_hash = 2002
        };

        Messages_MessagesBase slice = new Messages_MessagesSlice
        {
            messages = Array.Empty<MessageBase>(),
            users = new Dictionary<long, User> { [user.id] = user },
            chats = new Dictionary<long, ChatBase>
            {
                [chat.id] = chat,
                [channel.id] = channel
            }
        };

        store.Merge(slice);

        store.TryGet(PeerId.FromUser(11), out var userPeer).Should().BeTrue();
        userPeer.Should().BeOfType<InputPeerUser>();

        store.TryGet(PeerId.FromChat(22), out var chatPeer).Should().BeTrue();
        chatPeer.Should().BeOfType<InputPeerChat>();

        store.TryGet(PeerId.FromChannel(33), out var channelPeer).Should().BeTrue();
        channelPeer.Should().BeOfType<InputPeerChannel>();
    }

    [Fact]
    public void Merge_Messages_Messages_and_ChannelMessages()
    {
        var store = new TelegramPeerStore();

        store.Merge(new Messages_Messages
        {
            messages = Array.Empty<MessageBase>(),
            users = new Dictionary<long, User>
            {
                [1] = new User { id = 1, flags = User.Flags.has_access_hash, access_hash = 9 }
            },
            chats = new Dictionary<long, ChatBase>()
        });

        store.TryGet(PeerId.FromUser(1), out _).Should().BeTrue();

        store.Merge(new Messages_ChannelMessages
        {
            messages = Array.Empty<MessageBase>(),
            users = new Dictionary<long, User>(),
            chats = new Dictionary<long, ChatBase>
            {
                [5] = new Channel
                {
                    id = 5,
                    title = "c",
                    flags = Channel.Flags.has_access_hash,
                    access_hash = 7
                }
            }
        });

        store.TryGet(PeerId.FromChannel(5), out _).Should().BeTrue();
    }
}
