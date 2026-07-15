namespace TgTui.Core.Paths;

public sealed class AppPaths
{
    private AppPaths(string root)
    {
        Root = root;
        ConfigFile = Path.Combine(root, "config.toml");
        SessionFile = Path.Combine(root, "session.dat");
        DraftsFile = Path.Combine(root, "drafts.json");
        MediaCacheDir = Path.Combine(root, "media");
        LogsDir = Path.Combine(root, "logs");
    }

    public string Root { get; }
    public string ConfigFile { get; }
    public string SessionFile { get; }
    public string DraftsFile { get; }
    public string MediaCacheDir { get; }
    public string LogsDir { get; }

    public static AppPaths ForRoot(string root) => new(root);

    public static AppPaths ForCurrentUser()
    {
        var root = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tg-tui")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "tg-tui");
        return new AppPaths(root);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(MediaCacheDir);
        Directory.CreateDirectory(LogsDir);
    }
}
