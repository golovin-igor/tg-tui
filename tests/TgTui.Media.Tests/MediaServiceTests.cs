using FluentAssertions;
using TgTui.Core.Models;
using TgTui.Media;

namespace TgTui.Media.Tests;

public class MediaServiceTests
{
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
        var service = new MediaService(new MediaCache(Path.Combine(Path.GetTempPath(), "unused-cache")));
        var missing = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".png");
        service.RenderPreview(missing, 20).Should().Be("🖼 image unavailable");
    }
}
