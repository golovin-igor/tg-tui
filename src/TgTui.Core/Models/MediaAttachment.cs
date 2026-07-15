namespace TgTui.Core.Models;

public sealed class MediaAttachment
{
    public required string Kind { get; init; }
    public string? FileName { get; init; }
    public string? LocalPath { get; init; }
    public string? MimeType { get; init; }
    public long? SizeBytes { get; init; }

    /// <summary>Chat that owns the message containing this media (for on-demand download).</summary>
    public ChatId? SourceChatId { get; init; }

    /// <summary>Message that contains this media (for on-demand download).</summary>
    public MessageId? SourceMessageId { get; init; }
}
