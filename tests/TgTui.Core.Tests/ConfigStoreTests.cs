using FluentAssertions;
using TgTui.Core.Config;

public class ConfigStoreTests
{
    [Fact]
    public async Task Save_and_load_roundtrip_api_id_and_hash()
    {
        var root = Path.Combine(Path.GetTempPath(), "tg-tui-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.toml");
        try
        {
            var store = new ConfigStore(path);
            store.Current.ApiId = 12345;
            store.Current.ApiHash = "0123456789abcdef0123456789abcdef";
            await store.SaveAsync();

            File.Exists(path).Should().BeTrue();
            var text = await File.ReadAllTextAsync(path);
            text.Should().Contain("api_id");
            text.Should().Contain("12345");
            text.Should().Contain("api_hash");
            text.Should().Contain("0123456789abcdef0123456789abcdef");

            var store2 = new ConfigStore(path);
            await store2.LoadAsync();
            store2.Current.ApiId.Should().Be(12345);
            store2.Current.ApiHash.Should().Be("0123456789abcdef0123456789abcdef");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Load_missing_file_yields_empty_config()
    {
        var root = Path.Combine(Path.GetTempPath(), "tg-tui-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.toml");
        try
        {
            var store = new ConfigStore(path);
            await store.LoadAsync();
            store.Current.ApiId.Should().BeNull();
            store.Current.ApiHash.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
