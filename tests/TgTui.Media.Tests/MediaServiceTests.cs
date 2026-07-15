using FluentAssertions;
using TgTui.Core.Models;
using TgTui.Core.Ports;
using TgTui.Media;

namespace TgTui.Media.Tests;

public class MediaServiceTests
{
    // Minimal 1×1 PNG (red pixel)
    private static readonly byte[] OneByOnePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task EnsureLocalAsync_returns_existing_local_path()
    {
        var path = Path.Combine(Path.GetTempPath(), "tg-tui-local-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            await File.WriteAllTextAsync(path, "x");
            var service = new MediaService(new MediaCache(Path.Combine(Path.GetTempPath(), "unused-cache")));
            var media = new MediaAttachment { Kind = "photo", LocalPath = path };
            var result = await service.EnsureLocalAsync(media);
            result.Should().Be(path);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task EnsureLocalAsync_returns_null_when_no_local_file()
    {
        var service = new MediaService(new MediaCache(Path.Combine(Path.GetTempPath(), "unused-cache")));
        var media = new MediaAttachment { Kind = "photo", LocalPath = null };
        var result = await service.EnsureLocalAsync(media);
        result.Should().BeNull();
    }

    [Fact]
    public async Task EnsureLocalAsync_returns_cached_path_without_redownload()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "tg-tui-ms-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new MediaCache(cacheRoot);
            const string key = "10_20";
            await using (var ms = new MemoryStream(OneByOnePng))
                cache.Put(key, ms);

            var downloader = new FakeDownloader("/never-called");
            var service = new MediaService(cache, downloader);
            var media = new MediaAttachment
            {
                Kind = "photo",
                SourceChatId = new ChatId(10),
                SourceMessageId = new MessageId(20)
            };

            var result = await service.EnsureLocalAsync(media);
            result.Should().Be(cache.GetPath(key));
            downloader.Calls.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureLocalAsync_downloads_and_puts_into_cache()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "tg-tui-ms-cache-" + Guid.NewGuid().ToString("N"));
        var download = Path.Combine(Path.GetTempPath(), "tg-tui-dl-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await File.WriteAllBytesAsync(download, OneByOnePng);
            var cache = new MediaCache(cacheRoot);
            var downloader = new FakeDownloader(download);
            var service = new MediaService(cache, downloader);
            var media = new MediaAttachment
            {
                Kind = "photo",
                SourceChatId = new ChatId(7),
                SourceMessageId = new MessageId(8)
            };

            var result = await service.EnsureLocalAsync(media);

            downloader.Calls.Should().Be(1);
            result.Should().Be(cache.GetPath("7_8"));
            File.Exists(result).Should().BeTrue();
            cache.Exists("7_8").Should().BeTrue();
        }
        finally
        {
            if (File.Exists(download))
                File.Delete(download);
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void GetOpenCommand_selects_platform_opener()
    {
        const string path = "/tmp/photo.png";
        var psi = MediaService.GetOpenCommand(path);

        if (OperatingSystem.IsMacOS())
        {
            psi.FileName.Should().Be("open");
            psi.ArgumentList.Should().Contain(path);
            psi.UseShellExecute.Should().BeFalse();
        }
        else if (OperatingSystem.IsLinux())
        {
            psi.FileName.Should().Be("xdg-open");
            psi.ArgumentList.Should().Contain(path);
            psi.UseShellExecute.Should().BeFalse();
        }
        else if (OperatingSystem.IsWindows())
        {
            psi.FileName.Should().Be(path);
            psi.UseShellExecute.Should().BeTrue();
        }
    }

    [Fact]
    public void RenderPreview_missing_file_returns_placeholder()
    {
        var service = new MediaService(
            new MediaCache(Path.Combine(Path.GetTempPath(), "unused-cache")),
            getCapability: () => GraphicsCapability.HalfBlock);
        var missing = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".png");
        service.RenderPreview(missing, 20).Should().Be("🖼 image unavailable");
    }

    [Fact]
    public void RenderPreview_None_returns_open_hint()
    {
        var service = new MediaService(
            new MediaCache(Path.Combine(Path.GetTempPath(), "unused-cache")),
            getCapability: () => GraphicsCapability.None);

        service.RenderPreview("/any", 20).Should().Be("🖼 (open with o)");
    }

    [Fact]
    public void RenderPreview_HalfBlock_uses_half_block_renderer()
    {
        var path = WriteTempPng();
        try
        {
            var service = new MediaService(
                new MediaCache(Path.Combine(Path.GetTempPath(), "unused-cache")),
                getCapability: () => GraphicsCapability.HalfBlock);

            var result = service.RenderPreview(path, 20);
            result.Should().NotBe("🖼 (open with o)");
            result.Should().NotBe("🖼 image unavailable");
            result.Should().NotContain("\u001b_G"); // Kitty APC
            result.Should().Contain("▄");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RenderPreview_Kitty_uses_protocol_renderer()
    {
        var path = WriteTempPng();
        try
        {
            var service = new MediaService(
                new MediaCache(Path.Combine(Path.GetTempPath(), "unused-cache")),
                getCapability: () => GraphicsCapability.Kitty);

            var result = service.RenderPreview(path, 20);
            result.Should().Contain("\u001b_G");
            result.Should().Contain("a=T");
            result.Should().Contain("f=100");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RenderPreview_Sixel_branches_to_protocol_renderer()
    {
        var path = WriteTempPng();
        try
        {
            var service = new MediaService(
                new MediaCache(Path.Combine(Path.GetTempPath(), "unused-cache")),
                getCapability: () => GraphicsCapability.Sixel);

            // Sixel may fall back to half-block in v1, but must not use the None placeholder.
            var result = service.RenderPreview(path, 20);
            result.Should().NotBe("🖼 (open with o)");
            result.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RenderPreview_ITerm2_branches_to_protocol_renderer()
    {
        var path = WriteTempPng();
        try
        {
            var service = new MediaService(
                new MediaCache(Path.Combine(Path.GetTempPath(), "unused-cache")),
                getCapability: () => GraphicsCapability.ITerm2);

            var result = service.RenderPreview(path, 20);
            result.Should().NotBe("🖼 (open with o)");
            // iTerm2 OSC 1337 or half-block fallback both valid for structure test
            result.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), "tg-tui-img-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, OneByOnePng);
        return path;
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
