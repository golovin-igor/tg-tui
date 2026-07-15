namespace TgTui.Core.Models;

public sealed class DialogItem
{
    public required ChatId Id { get; init; }
    public required string Title { get; init; }
    public string LastMessagePreview { get; init; } = "";
    public DateTimeOffset? LastMessageAt { get; init; }
    public int UnreadCount { get; init; }
    public bool IsPinned { get; init; }
    public bool IsMuted { get; init; }
    public required char AvatarLetter { get; init; }
}
