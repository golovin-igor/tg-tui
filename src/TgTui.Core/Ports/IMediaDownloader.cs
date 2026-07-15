using TgTui.Core.Models;

namespace TgTui.Core.Ports;

public interface IMediaDownloader
{
    Task<string> DownloadMessageMediaAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken = default);
}
