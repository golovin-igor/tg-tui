using FluentAssertions;
using TgTui.Core.Paths;

public class AppPathsTests
{
    [Fact]
    public void EnsureCreated_creates_root_and_subdirs()
    {
        var root = Path.Combine(Path.GetTempPath(), "tg-tui-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.ForRoot(root);
            paths.EnsureCreated();
            Directory.Exists(paths.Root).Should().BeTrue();
            Directory.Exists(paths.MediaCacheDir).Should().BeTrue();
            Directory.Exists(paths.LogsDir).Should().BeTrue();
            paths.ConfigFile.Should().EndWith("config.toml");
            paths.SessionFile.Should().EndWith("session.dat");
            paths.DraftsFile.Should().EndWith("drafts.json");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
