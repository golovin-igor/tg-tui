using FluentAssertions;
using TgTui.Core.Models;
using TgTui.Core.Ports;
using TgTui.Media;

namespace TgTui.Media.Tests;

public class MediaServiceDownloaderTests
{
    [Fact]
    public async Task EnsureLocalAsync_uses_downloader_when_no_local_path()
    {
        var expected = Path.Combine(Path.GetTempPath(), "tg-tui-dl-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllTextAsync(expected, "data");
        try
        {
            var downloader = new FakeDownloader(expected);
            var service = new MediaService(new MediaCache(Path.Combine(Path.GetTempPath(), "cache")), downloader);
            var media = new MediaAttachment
            {
                Kind = "photo",
                SourceChatId = new ChatId(1),
                SourceMessageId = new MessageId(2)
            };

            var result = await service.EnsureLocalAsync(media);
            result.Should().Be(expected);
            downloader.Calls.Should().Be(1);
        }
        finally
        {
            if (File.Exists(expected))
                File.Delete(expected);
        }
    }

    [Fact]
    public async Task EnsureLocalAsync_prefers_existing_local_over_downloader()
    {
        var local = Path.Combine(Path.GetTempPath(), "tg-tui-local2-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllTextAsync(local, "x");
        try
        {
            var downloader = new FakeDownloader("/never");
            var service = new MediaService(new MediaCache(Path.Combine(Path.GetTempPath(), "cache")), downloader);
            var media = new MediaAttachment { Kind = "photo", LocalPath = local };

            var result = await service.EnsureLocalAsync(media);
            result.Should().Be(local);
            downloader.Calls.Should().Be(0);
        }
        finally
        {
            if (File.Exists(local))
                File.Delete(local);
        }
    }

    private sealed class FakeDownloader : IMediaDownloader
    {
        private readonly string _path;

        public FakeDownloader(string path) => _path = path;

        public int Calls { get; private set; }

        public Task<string> DownloadMessageMediaAsync(
            ChatId chatId,
            MessageId messageId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_path);
        }
    }
}
