using Tomlyn;
using TgTui.Core.Paths;

namespace TgTui.Core.Config;

public sealed class ConfigStore
{
    private readonly string _path;

    public ConfigStore(string path)
    {
        _path = path;
    }

    /// <summary>DI-friendly constructor using <see cref="AppPaths.ConfigFile"/>.</summary>
    public ConfigStore(AppPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).ConfigFile)
    {
    }

    public AppConfig Current { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            Current = new AppConfig();
            return;
        }

        var text = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
        Current = string.IsNullOrWhiteSpace(text)
            ? new AppConfig()
            : TomlSerializer.Deserialize<AppConfig>(text) ?? new AppConfig();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var text = TomlSerializer.Serialize(Current);
        var tempPath = _path + ".tmp";
        await File.WriteAllTextAsync(tempPath, text, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _path, overwrite: true);
    }
}
