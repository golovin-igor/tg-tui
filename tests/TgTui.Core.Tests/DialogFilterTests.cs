using FluentAssertions;
using TgTui.Core.Filtering;
using TgTui.Core.Models;

public class DialogFilterTests
{
    private static DialogItem D(string title, bool pinned = false) => new()
    {
        Id = new ChatId(title.GetHashCode()),
        Title = title,
        LastMessagePreview = "",
        AvatarLetter = title[0],
        IsPinned = pinned
    };

    [Fact]
    public void Empty_query_returns_all_preserving_order()
    {
        var items = new[] { D("Alice"), D("Bob") };
        DialogFilter.Apply(items, null).Should().Equal(items);
        DialogFilter.Apply(items, "  ").Should().Equal(items);
    }

    [Fact]
    public void Filters_case_insensitive_by_title()
    {
        var items = new[] { D("Alice"), D("Bob"), D("Alicia") };
        DialogFilter.Apply(items, "ali").Select(x => x.Title)
            .Should().Equal("Alice", "Alicia");
    }
}
