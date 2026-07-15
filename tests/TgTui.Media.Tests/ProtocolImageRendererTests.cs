using FluentAssertions;
using TgTui.Media;

namespace TgTui.Media.Tests;

public class ProtocolImageRendererTests
{
    private static readonly byte[] OneByOnePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void RenderFile_missing_returns_placeholder()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".png");
        ProtocolImageRenderer.RenderFile(missing, 20, GraphicsCapability.Kitty)
            .Should().Be("🖼 image unavailable");
    }

    [Fact]
    public void RenderFile_Kitty_emits_graphics_protocol()
    {
        var path = Path.Combine(Path.GetTempPath(), "tg-tui-kitty-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, OneByOnePng);
            var result = ProtocolImageRenderer.RenderFile(path, 12, GraphicsCapability.Kitty);
            result.Should().StartWith("\u001b_G");
            result.Should().Contain("a=T");
            result.Should().Contain("f=100");
            result.Should().Contain("c=12");
            result.Should().EndWith("\u001b\\");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void RenderFile_ITerm2_emits_osc_1337()
    {
        var path = Path.Combine(Path.GetTempPath(), "tg-tui-iterm-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, OneByOnePng);
            var result = ProtocolImageRenderer.RenderFile(path, 12, GraphicsCapability.ITerm2);
            result.Should().Contain("\u001b]1337;File=");
            result.Should().Contain("inline=1");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void RenderFile_Sixel_does_not_throw()
    {
        var path = Path.Combine(Path.GetTempPath(), "tg-tui-sixel-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, OneByOnePng);
            var act = () => ProtocolImageRenderer.RenderFile(path, 12, GraphicsCapability.Sixel);
            act.Should().NotThrow();
            act().Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
