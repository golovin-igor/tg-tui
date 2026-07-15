using FluentAssertions;
using System.Text;
using TgTui.Media;

namespace TgTui.Media.Tests;

public class MediaCacheTests
{
    [Fact]
    public void Put_get_exists_roundtrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "tg-tui-media-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new MediaCache(root);
            const string key = "abc123";
            cache.Exists(key).Should().BeFalse();

            var bytes = Encoding.UTF8.GetBytes("hello-media");
            using (var stream = new MemoryStream(bytes))
                cache.Put(key, stream);

            cache.Exists(key).Should().BeTrue();
            var path = cache.GetPath(key);
            path.Should().StartWith(root);
            File.Exists(path).Should().BeTrue();
            File.ReadAllBytes(path).Should().Equal(bytes);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
