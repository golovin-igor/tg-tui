namespace TgTui.Core.Models;

public sealed class MediaAttachment
{
    public required string Kind { get; init; }
    public string? FileName { get; init; }
    public string? LocalPath { get; init; }
    public string? MimeType { get; init; }
    public long? SizeBytes { get; init; }
}
