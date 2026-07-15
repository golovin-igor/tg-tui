using FluentAssertions;
using TgTui.Media;

namespace TgTui.Media.Tests;

public class HalfBlockImageRendererTests
{
    // Minimal 1×1 PNG (red pixel)
    private static readonly byte[] OneByOnePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void RenderFile_missing_returns_placeholder()
    {
        var path = Path.Combine(Path.GetTempPath(), "tg-tui-missing-" + Guid.NewGuid().ToString("N") + ".png");
        HalfBlockImageRenderer.RenderFile(path, 40).Should().Be("🖼 image unavailable");
    }

    [Fact]
    public void RenderFile_valid_png_returns_non_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), "tg-tui-img-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, OneByOnePng);
            var result = HalfBlockImageRenderer.RenderFile(path, 40);
            result.Should().NotBeNullOrWhiteSpace();
            result.Should().NotBe("🖼 image unavailable");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void RenderFile_does_not_throw_on_valid_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "tg-tui-img-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, OneByOnePng);
            var act = () => HalfBlockImageRenderer.RenderFile(path, 10);
            act.Should().NotThrow();
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
