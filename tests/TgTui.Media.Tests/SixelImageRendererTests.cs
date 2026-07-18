using FluentAssertions;
using TgTui.Core.Events;
using TgTui.Core.Models;
using TgTui.Media;

namespace TgTui.Media.Tests;

public class SixelImageRendererTests
{
    private static readonly byte[] OneByOnePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void RenderFile_emits_dcs_sixel_sequence()
    {
        var path = Path.Combine(Path.GetTempPath(), "tg-tui-sixel-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, OneByOnePng);
            var result = SixelImageRenderer.RenderFile(path, 12);
            result.Should().StartWith("\u001bPq");
            result.Should().Contain("#0;2;");
            result.Should().EndWith("\u001b\\");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
