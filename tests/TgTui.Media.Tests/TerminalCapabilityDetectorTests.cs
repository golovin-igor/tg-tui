using FluentAssertions;
using TgTui.Media;

namespace TgTui.Media.Tests;

public class TerminalCapabilityDetectorTests
{
    [Theory]
    [InlineData("kitty", "xterm-kitty", null, GraphicsCapability.Kitty)]
    [InlineData(null, "xterm-256color", "WezTerm", GraphicsCapability.Sixel)]
    [InlineData(null, "xterm-256color", "iTerm.app", GraphicsCapability.ITerm2)]
    [InlineData(null, "dumb", null, GraphicsCapability.HalfBlock)]
    public void Detects(string? kittyWindow, string? term, string? termProgram, GraphicsCapability expected)
    {
        var env = new Dictionary<string, string?> { ["TERM"] = term };
        if (kittyWindow is not null)
            env["KITTY_WINDOW_ID"] = kittyWindow;

        new TerminalCapabilityDetector().Detect(env, termProgram).Should().Be(expected);
    }

    [Fact]
    public void Override_none()
    {
        var env = new Dictionary<string, string?>
        {
            ["TG_TUI_GRAPHICS"] = "none",
            ["TERM"] = "xterm-kitty"
        };

        new TerminalCapabilityDetector().Detect(env, null).Should().Be(GraphicsCapability.None);
    }

    [Theory]
    [InlineData("kitty", GraphicsCapability.Kitty)]
    [InlineData("sixel", GraphicsCapability.Sixel)]
    [InlineData("iterm", GraphicsCapability.ITerm2)]
    [InlineData("half", GraphicsCapability.HalfBlock)]
    public void Override_values(string value, GraphicsCapability expected)
    {
        var env = new Dictionary<string, string?>
        {
            ["TG_TUI_GRAPHICS"] = value,
            ["TERM"] = "dumb"
        };

        new TerminalCapabilityDetector().Detect(env, null).Should().Be(expected);
    }

    [Fact]
    public void Kitty_from_TERM_containing_kitty()
    {
        var env = new Dictionary<string, string?> { ["TERM"] = "xterm-kitty" };
        new TerminalCapabilityDetector().Detect(env, null).Should().Be(GraphicsCapability.Kitty);
    }

    [Theory]
    [InlineData("foot")]
    [InlineData("wezterm")]
    [InlineData("mlterm")]
    [InlineData("contour")]
    [InlineData("xterm-256color")]
    public void Sixel_from_known_terms(string term)
    {
        var env = new Dictionary<string, string?> { ["TERM"] = term };
        new TerminalCapabilityDetector().Detect(env, null).Should().Be(GraphicsCapability.Sixel);
    }
}
