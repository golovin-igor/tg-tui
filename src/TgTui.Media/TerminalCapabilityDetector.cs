namespace TgTui.Media;

public sealed class TerminalCapabilityDetector
{
    public GraphicsCapability Detect(
        IReadOnlyDictionary<string, string?> environment,
        string? termProgram = null)
    {
        if (TryGetOverride(environment, out var forced))
            return forced;

        environment.TryGetValue("TERM", out var term);
        term ??= string.Empty;

        var resolvedTermProgram = termProgram
            ?? (environment.TryGetValue("TERM_PROGRAM", out var tp) ? tp : null)
            ?? string.Empty;

        if (IsKitty(environment, term))
            return GraphicsCapability.Kitty;

        if (string.Equals(resolvedTermProgram, "iTerm.app", StringComparison.OrdinalIgnoreCase))
            return GraphicsCapability.ITerm2;

        if (IsSixel(environment, term, resolvedTermProgram))
            return GraphicsCapability.Sixel;

        return GraphicsCapability.HalfBlock;
    }

    private static bool TryGetOverride(
        IReadOnlyDictionary<string, string?> environment,
        out GraphicsCapability capability)
    {
        capability = GraphicsCapability.None;
        if (!environment.TryGetValue("TG_TUI_GRAPHICS", out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "none":
                capability = GraphicsCapability.None;
                return true;
            case "half":
                capability = GraphicsCapability.HalfBlock;
                return true;
            case "sixel":
                capability = GraphicsCapability.Sixel;
                return true;
            case "kitty":
                capability = GraphicsCapability.Kitty;
                return true;
            case "iterm":
                capability = GraphicsCapability.ITerm2;
                return true;
            default:
                return false;
        }
    }

    private static bool IsKitty(IReadOnlyDictionary<string, string?> environment, string term)
    {
        if (environment.TryGetValue("KITTY_WINDOW_ID", out var kittyId)
            && !string.IsNullOrEmpty(kittyId))
            return true;

        return term.Contains("kitty", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSixel(
        IReadOnlyDictionary<string, string?> environment,
        string term,
        string termProgram)
    {
        if (string.Equals(termProgram, "WezTerm", StringComparison.OrdinalIgnoreCase))
            return true;

        if (term.Equals("xterm-256color", StringComparison.OrdinalIgnoreCase)
            || term.Equals("foot", StringComparison.OrdinalIgnoreCase)
            || term.Equals("wezterm", StringComparison.OrdinalIgnoreCase)
            || term.Equals("mlterm", StringComparison.OrdinalIgnoreCase)
            || term.Equals("contour", StringComparison.OrdinalIgnoreCase)
            || term.Contains("foot", StringComparison.OrdinalIgnoreCase)
            || term.Contains("wezterm", StringComparison.OrdinalIgnoreCase)
            || term.Contains("mlterm", StringComparison.OrdinalIgnoreCase)
            || term.Contains("contour", StringComparison.OrdinalIgnoreCase))
            return true;

        // Windows Terminal often reports xterm-256color with WT_SESSION set
        if (environment.TryGetValue("WT_SESSION", out var wt) && !string.IsNullOrEmpty(wt)
            && term.Contains("xterm", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
