using TgTui.Core.Models;
using TgTui.Core.Ports;
using TL;
using WTelegram;

namespace TgTui.Telegram;

public sealed class WTelegramMediaDownloader : IMediaDownloader
{
    private readonly TelegramSession _session;
    private readonly TelegramPeerStore _peers;
    private readonly string _cacheDirectory;

    public WTelegramMediaDownloader(
        TelegramSession session,
        TelegramPeerStore peers,
        string cacheDirectory)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _cacheDirectory = cacheDirectory;
    }

    public async Task<string> DownloadMessageMediaAsync(
        ChatId chatId,
        MessageId messageId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = _session.RequireClient();
        var peer = _peers.Require(chatId);
        var id = checked((int)messageId.Value);

        var bundle = await client.GetMessages(peer, new InputMessage[] { new InputMessageID { id = id } })
            .ConfigureAwait(false);

        _peers.Merge(bundle);

        var msg = bundle.Messages.OfType<Message>().FirstOrDefault(m => m.id == id)
            ?? throw new InvalidOperationException($"Message {messageId.Value} not found in chat {chatId.Value}.");

        Directory.CreateDirectory(_cacheDirectory);
        var baseName = $"{chatId.Value}_{messageId.Value}";

        switch (msg.media)
        {
            case MessageMediaPhoto { photo: Photo photo }:
            {
                var path = Path.Combine(_cacheDirectory, baseName + ".jpg");
                await using (var fs = File.Create(path))
                {
                    await client.DownloadFileAsync(photo, fs).ConfigureAwait(false);
                }
                return path;
            }

            case MessageMediaDocument { document: Document doc }:
            {
                var ext = ExtensionFor(doc);
                var path = Path.Combine(_cacheDirectory, baseName + ext);
                await using (var fs = File.Create(path))
                {
                    await client.DownloadFileAsync(doc, fs).ConfigureAwait(false);
                }
                return path;
            }

            default:
                throw new InvalidOperationException("Message has no downloadable media.");
        }
    }

    private static string ExtensionFor(Document doc)
    {
        var fromName = Path.GetExtension(doc.Filename ?? string.Empty);
        if (!string.IsNullOrEmpty(fromName))
            return fromName;

        return doc.mime_type switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "video/mp4" => ".mp4",
            "audio/ogg" => ".ogg",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };
    }
}
