namespace TgTui.Core.Models;

/// <summary>
/// Builds dialog-list preview strings from domain messages (no Telegram types).
/// </summary>
public static class ChatMessagePreview
{
    public const string PhotoPreview = "📷 Photo";
    public const string FilePreview = "📎 File";
    public const string StickerPreview = "Sticker";

    public static string FromMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!string.IsNullOrWhiteSpace(message.Text))
            return CollapseWhitespace(message.Text);

        if (message.Media is not { } media)
            return string.Empty;

        if (string.Equals(media.Kind, "photo", StringComparison.OrdinalIgnoreCase))
            return PhotoPreview;

        if (string.Equals(media.Kind, "sticker", StringComparison.OrdinalIgnoreCase))
            return StickerPreview;

        if (!string.IsNullOrWhiteSpace(media.FileName))
            return media.FileName;

        return FilePreview;
    }

    private static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
