using FluentAssertions;
using TgTui.Core.Drafts;
using TgTui.Core.Models;

public class FileDraftStoreTests
{
    [Fact]
    public async Task Set_get_and_persist_roundtrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "tg-tui-drafts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "drafts.json");
        try
        {
            var store = new FileDraftStore(path);
            await store.LoadAsync();
            store.SetDraft(new ChatId(1), "hello");
            store.GetDraft(new ChatId(1)).Should().Be("hello");
            await store.SaveAsync();

            var store2 = new FileDraftStore(path);
            await store2.LoadAsync();
            store2.GetDraft(new ChatId(1)).Should().Be("hello");

            store2.SetDraft(new ChatId(1), null);
            store2.GetDraft(new ChatId(1)).Should().BeNull();
            await store2.SaveAsync();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
