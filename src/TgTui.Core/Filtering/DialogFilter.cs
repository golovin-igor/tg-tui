using TgTui.Core.Models;

namespace TgTui.Core.Filtering;

public static class DialogFilter
{
    public static IReadOnlyList<DialogItem> Apply(IReadOnlyList<DialogItem> source, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return source;
        var q = query.Trim();
        return source.Where(d => d.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
