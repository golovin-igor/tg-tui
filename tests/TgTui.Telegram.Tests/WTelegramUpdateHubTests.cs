using FluentAssertions;
using TgTui.Core.Events;
using TgTui.Core.Models;
using TgTui.Telegram;
using TgTui.Telegram.Mapping;
using TL;

namespace TgTui.Telegram.Tests;

public class WTelegramUpdateHubTests
{
    [Fact]
    public void HandleUpdates_new_message_fires_messages_and_dialogs()
    {
        var hub = new WTelegramUpdateHub();
        var messages = new List<MessagesChanged>();
        var dialogs = new List<DialogsChanged>();
        hub.MessagesChanged += messages.Add;
        hub.DialogsChanged += dialogs.Add;

        var updates = new Updates
        {
            updates = new Update[]
            {
                new UpdateNewMessage
                {
                    message = new Message
                    {
                        id = 1,
                        message = "hi",
                        peer_id = new PeerUser { user_id = 55 },
                        date = DateTime.UtcNow
                    }
                }
            },
            users = new Dictionary<long, User>(),
            chats = new Dictionary<long, ChatBase>()
        };

        hub.HandleUpdates(updates);

        messages.Should().ContainSingle(m => m.ChatId == PeerId.FromUser(55));
        dialogs.Should().ContainSingle();
    }

    [Fact]
    public void HandleUpdates_read_inbox_refreshes_dialog_and_chat()
    {
        var hub = new WTelegramUpdateHub();
        var messages = new List<MessagesChanged>();
        var dialogs = new List<DialogsChanged>();
        hub.MessagesChanged += messages.Add;
        hub.DialogsChanged += dialogs.Add;

        hub.HandleUpdates(new Updates
        {
            updates = new Update[]
            {
                new UpdateReadHistoryInbox
                {
                    peer = new PeerChat { chat_id = 8 },
                    max_id = 10
                }
            },
            users = new Dictionary<long, User>(),
            chats = new Dictionary<long, ChatBase>()
        });

        messages.Should().ContainSingle(m => m.ChatId == PeerId.FromChat(8));
        dialogs.Should().ContainSingle();
    }

    [Fact]
    public void HandleUpdates_channel_message_uses_channel_chat_id()
    {
        var hub = new WTelegramUpdateHub();
        ChatId? seen = null;
        hub.MessagesChanged += e => seen = e.ChatId;

        hub.HandleUpdates(new Updates
        {
            updates = new Update[]
            {
                new UpdateNewChannelMessage
                {
                    message = new Message
                    {
                        id = 2,
                        peer_id = new PeerChannel { channel_id = 100 },
                        message = "post",
                        date = DateTime.UtcNow
                    }
                }
            },
            users = new Dictionary<long, User>(),
            chats = new Dictionary<long, ChatBase>()
        });

        seen.Should().Be(PeerId.FromChannel(100));
    }

    [Fact]
    public void Publish_helpers_raise_events()
    {
        var hub = new WTelegramUpdateHub();
        ConnectionStateChanged? conn = null;
        hub.ConnectionStateChanged += e => conn = e;
        hub.Publish(new ConnectionStateChanged(true, "ok"));
        conn.Should().NotBeNull();
        conn!.IsConnected.Should().BeTrue();
        conn.Detail.Should().Be("ok");
    }

    [Fact]
    public async Task StartAsync_without_session_is_noop()
    {
        var hub = new WTelegramUpdateHub();
        await hub.StartAsync();
    }
}
