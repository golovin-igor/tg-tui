using FluentAssertions;
using TgTui.Core.Models;
using TgTui.Telegram.Mapping;
using TL;

namespace TgTui.Telegram.Tests;

public class TelegramMapperTests
{
    [Theory]
    [InlineData(null, false, false, null, "")]
    [InlineData("", false, false, null, "")]
    [InlineData("hi", false, false, null, "hi")]
    [InlineData("hi", true, false, null, "hi")]
    [InlineData(null, true, false, null, TelegramMapper.PhotoPreview)]
    [InlineData(null, false, true, "file.pdf", TelegramMapper.FilePreview)]
    [InlineData(null, false, true, null, TelegramMapper.FilePreview)]
    [InlineData(null, false, true, "sticker", TelegramMapper.StickerPreview)]
    [InlineData(null, false, true, "STICKER", TelegramMapper.StickerPreview)]
    public void Preview_maps_text_and_media(
        string? text,
        bool hasPhoto,
        bool hasDocument,
        string? documentName,
        string expected)
    {
        TelegramMapper.Preview(text, hasPhoto, hasDocument, documentName).Should().Be(expected);
    }

    [Theory]
    [InlineData("alice", 'A')]
    [InlineData("  bob", 'B')]
    [InlineData("ёлка", 'Ё')]
    [InlineData("", '?')]
    [InlineData("   ", '?')]
    public void AvatarLetter_uses_first_title_char(string title, char expected)
    {
        TelegramMapper.AvatarLetter(title).Should().Be(expected);
    }

    [Fact]
    public void SortDialogs_pinned_first_then_last_message_desc()
    {
        var older = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var newer = DateTimeOffset.Parse("2024-06-01T00:00:00Z");
        var mid = DateTimeOffset.Parse("2024-03-01T00:00:00Z");

        var items = new[]
        {
            D("unpinned-old", pinned: false, at: older),
            D("pinned-mid", pinned: true, at: mid),
            D("unpinned-new", pinned: false, at: newer),
            D("pinned-new", pinned: true, at: newer),
            D("unpinned-null", pinned: false, at: null),
        };

        var sorted = TelegramMapper.SortDialogs(items);

        sorted.Select(x => x.Title).Should().Equal(
            "pinned-new",
            "pinned-mid",
            "unpinned-new",
            "unpinned-old",
            "unpinned-null");
    }

    [Fact]
    public void FormatUserTitle_full_name_and_deleted()
    {
        TelegramMapper.FormatUserTitle(null).Should().Be(TelegramMapper.DeletedAccountTitle);
        TelegramMapper.FormatUserTitle(new UserEmpty { id = 1 }).Should().Be(TelegramMapper.DeletedAccountTitle);

        var user = new User { id = 2, first_name = "Ada", last_name = "Lovelace" };
        TelegramMapper.FormatUserTitle(user).Should().Be("Ada Lovelace");
        TelegramMapper.FormatUserTitle(user, selfUserId: 2).Should().Be(TelegramMapper.SavedMessagesTitle);
    }

    [Fact]
    public void FormatChatTitle_uses_title()
    {
        TelegramMapper.FormatChatTitle(null).Should().Be(TelegramMapper.DeletedAccountTitle);
        TelegramMapper.FormatChatTitle(new Chat { id = 1, title = "Group" }).Should().Be("Group");
        TelegramMapper.FormatChatTitle(new Channel { id = 9, title = "News" }).Should().Be("News");
    }

    [Fact]
    public void PeerId_encodes_user_chat_channel_without_collision()
    {
        PeerId.FromUser(42).Value.Should().Be(42);
        PeerId.FromChat(42).Value.Should().Be(-42);
        PeerId.FromChannel(42).Value.Should().Be(-(PeerId.ChannelOffset + 42));

        var userPeer = new PeerUser { user_id = 7 };
        var chatPeer = new PeerChat { chat_id = 7 };
        var channelPeer = new PeerChannel { channel_id = 7 };

        PeerId.FromPeer(userPeer).Value.Should().NotBe(PeerId.FromPeer(chatPeer).Value);
        PeerId.FromPeer(chatPeer).Value.Should().NotBe(PeerId.FromPeer(channelPeer).Value);
        PeerId.FromPeer(userPeer).Value.Should().NotBe(PeerId.FromPeer(channelPeer).Value);
    }

    [Fact]
    public void IndexTopMessagesByMarkedPeer_keeps_user_chat_channel_distinct()
    {
        // Same raw Peer.ID (=7) would collide; marked keys must not.
        const long rawId = 7;
        var userMsg = new Message
        {
            id = 10,
            message = "user",
            peer_id = new PeerUser { user_id = rawId },
            date = DateTime.UtcNow
        };
        var chatMsg = new Message
        {
            id = 20,
            message = "chat",
            peer_id = new PeerChat { chat_id = rawId },
            date = DateTime.UtcNow
        };
        var channelMsg = new Message
        {
            id = 30,
            message = "channel",
            peer_id = new PeerChannel { channel_id = rawId },
            date = DateTime.UtcNow
        };
        // Older user message with lower id — should lose to userMsg.
        var olderUser = new Message
        {
            id = 5,
            message = "older",
            peer_id = new PeerUser { user_id = rawId },
            date = DateTime.UtcNow
        };

        var index = TelegramMapper.IndexTopMessagesByMarkedPeer(
            new MessageBase[] { olderUser, userMsg, chatMsg, channelMsg });

        index.Should().HaveCount(3);
        index[PeerId.FromUser(rawId).Value].Should().BeSameAs(userMsg);
        index[PeerId.FromChat(rawId).Value].Should().BeSameAs(chatMsg);
        index[PeerId.FromChannel(rawId).Value].Should().BeSameAs(channelMsg);

        // Using raw Peer.ID would collapse all three into one key (=7).
        rawId.Should().Be(userMsg.Peer!.ID);
        rawId.Should().Be(chatMsg.Peer!.ID);
        rawId.Should().Be(channelMsg.Peer!.ID);
    }

    [Fact]
    public void ResolveTopMessage_uses_marked_peer_key()
    {
        const long rawId = 42;
        var userTop = new Message
        {
            id = 5,
            message = "u",
            peer_id = new PeerUser { user_id = rawId },
            date = DateTime.UtcNow
        };
        var chatTop = new Message
        {
            id = 5,
            message = "c",
            peer_id = new PeerChat { chat_id = rawId },
            date = DateTime.UtcNow
        };
        var messages = new MessageBase[] { userTop, chatTop };
        var index = TelegramMapper.IndexTopMessagesByMarkedPeer(messages);

        var userDialog = new Dialog
        {
            peer = new PeerUser { user_id = rawId },
            top_message = 5,
            notify_settings = new PeerNotifySettings()
        };
        var chatDialog = new Dialog
        {
            peer = new PeerChat { chat_id = rawId },
            top_message = 5,
            notify_settings = new PeerNotifySettings()
        };

        TelegramMapper.ResolveTopMessage(userDialog, index, messages).Should().BeSameAs(userTop);
        TelegramMapper.ResolveTopMessage(chatDialog, index, messages).Should().BeSameAs(chatTop);
    }

    [Fact]
    public void MapMessage_maps_outgoing_edit_reply_and_photo()
    {
        var msg = new Message
        {
            id = 10,
            message = "hello",
            date = new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            peer_id = new PeerUser { user_id = 5 },
            flags = Message.Flags.out_ | Message.Flags.has_edit_date | Message.Flags.has_reply_to | Message.Flags.has_media,
            edit_date = new DateTime(2024, 5, 1, 13, 0, 0, DateTimeKind.Utc),
            reply_to = new MessageReplyHeader { reply_to_msg_id = 3 },
            media = new MessageMediaPhoto()
        };

        var chatId = PeerId.FromUser(5);
        var mapped = TelegramMapper.MapMessage(msg, chatId, readInboxMaxId: 1, readOutboxMaxId: 10);

        mapped.Id.Value.Should().Be(10);
        mapped.ChatId.Should().Be(chatId);
        mapped.Text.Should().Be("hello");
        mapped.IsOutgoing.Should().BeTrue();
        mapped.IsEdited.Should().BeTrue();
        mapped.ReplyToId!.Value.Value.Should().Be(3);
        mapped.IsRead.Should().BeTrue();
        mapped.Media.Should().NotBeNull();
        mapped.Media!.Kind.Should().Be("photo");
        mapped.Media.SourceChatId.Should().Be(chatId);
        mapped.Media.SourceMessageId!.Value.Value.Should().Be(10);
        mapped.SentAt.UtcDateTime.Should().Be(new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void MapDialog_maps_pinned_unread_and_preview()
    {
        var dialog = new Dialog
        {
            peer = new PeerUser { user_id = 99 },
            top_message = 5,
            unread_count = 3,
            flags = Dialog.Flags.pinned,
            notify_settings = new PeerNotifySettings
            {
                flags = PeerNotifySettings.Flags.has_mute_until,
                mute_until = DateTime.UtcNow.AddDays(7)
            }
        };

        var user = new User { id = 99, first_name = "Zoe" };
        var top = new Message
        {
            id = 5,
            message = "",
            date = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            peer_id = new PeerUser { user_id = 99 },
            media = new MessageMediaPhoto()
        };

        var item = TelegramMapper.MapDialog(dialog, user, top);

        item.Id.Should().Be(PeerId.FromUser(99));
        item.Title.Should().Be("Zoe");
        item.UnreadCount.Should().Be(3);
        item.IsPinned.Should().BeTrue();
        item.IsMuted.Should().BeTrue();
        item.AvatarLetter.Should().Be('Z');
        item.LastMessagePreview.Should().Be(TelegramMapper.PhotoPreview);
        item.LastMessageAt.Should().NotBeNull();
    }

    [Fact]
    public void MapDialog_channel_uses_hash_avatar()
    {
        var dialog = new Dialog
        {
            peer = new PeerChannel { channel_id = 1 },
            top_message = 0,
            notify_settings = new PeerNotifySettings()
        };
        var channel = new Channel { id = 1, title = "Broadcast" };

        var item = TelegramMapper.MapDialog(dialog, channel, topMessage: null);
        item.AvatarLetter.Should().Be('#');
        item.Title.Should().Be("Broadcast");
    }

    [Fact]
    public void IsMuted_respects_mute_until()
    {
        TelegramMapper.IsMuted(null).Should().BeFalse();
        TelegramMapper.IsMuted(new PeerNotifySettings()).Should().BeFalse();
        TelegramMapper.IsMuted(new PeerNotifySettings
        {
            flags = PeerNotifySettings.Flags.has_mute_until,
            mute_until = DateTime.UtcNow.AddHours(-1)
        }).Should().BeFalse();
        TelegramMapper.IsMuted(new PeerNotifySettings
        {
            flags = PeerNotifySettings.Flags.has_mute_until,
            mute_until = DateTime.UtcNow.AddHours(1)
        }).Should().BeTrue();
    }

    [Fact]
    public void PreviewFromMessage_document_sticker()
    {
        var stickerDoc = new Document
        {
            id = 1,
            attributes = new DocumentAttribute[] { new DocumentAttributeSticker { alt = "😀" } }
        };
        var stickerMsg = new Message
        {
            id = 1,
            message = "",
            media = new MessageMediaDocument { document = stickerDoc }
        };
        TelegramMapper.PreviewFromMessage(stickerMsg).Should().Be(TelegramMapper.StickerPreview);

        var fileDoc = new Document
        {
            id = 2,
            attributes = new DocumentAttribute[] { new DocumentAttributeFilename { file_name = "a.bin" } }
        };
        var fileMsg = new Message
        {
            id = 2,
            message = "",
            media = new MessageMediaDocument { document = fileDoc }
        };
        TelegramMapper.PreviewFromMessage(fileMsg).Should().Be(TelegramMapper.FilePreview);
    }

    private static DialogItem D(string title, bool pinned, DateTimeOffset? at) => new()
    {
        Id = new ChatId(title.GetHashCode()),
        Title = title,
        IsPinned = pinned,
        LastMessageAt = at,
        AvatarLetter = TelegramMapper.AvatarLetter(title)
    };
}
