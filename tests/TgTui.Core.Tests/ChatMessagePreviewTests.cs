using FluentAssertions;
using TgTui.Core.Models;

namespace TgTui.Core.Tests;

public sealed class ChatMessagePreviewTests
{
    [Fact]
    public void FromMessage_prefers_text()
    {
        var preview = ChatMessagePreview.FromMessage(new ChatMessage
        {
            Id = new MessageId(1),
            ChatId = new ChatId(1),
            Text = "hello\nworld",
            IsOutgoing = true,
            SentAt = DateTimeOffset.Now,
        });

        preview.Should().Be("hello world");
    }

    [Fact]
    public void FromMessage_photo_without_text()
    {
        var preview = ChatMessagePreview.FromMessage(new ChatMessage
        {
            Id = new MessageId(1),
            ChatId = new ChatId(1),
            Text = "",
            IsOutgoing = false,
            SentAt = DateTimeOffset.Now,
            Media = new MediaAttachment { Kind = "photo" },
        });

        preview.Should().Be(ChatMessagePreview.PhotoPreview);
    }
}
