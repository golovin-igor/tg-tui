namespace TgTui.Core.Models;

public sealed class ChatMessage
{
    public required MessageId Id { get; init; }
    public required ChatId ChatId { get; init; }
    public required string Text { get; init; }
    public bool IsOutgoing { get; init; }
    public required DateTimeOffset SentAt { get; init; }
    public bool IsEdited { get; init; }
    public MessageId? ReplyToId { get; init; }
    public MediaAttachment? Media { get; init; }
    public bool IsRead { get; init; }
}
