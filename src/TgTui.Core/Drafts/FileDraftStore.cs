using System.Globalization;
using System.Text.Json;
using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.Core.Drafts;

public sealed class FileDraftStore : IDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly Dictionary<string, string> _drafts = new(StringComparer.Ordinal);

    public FileDraftStore(string path)
    {
        _path = path;
    }

    public string? GetDraft(ChatId chatId)
    {
        return _drafts.TryGetValue(Key(chatId), out var text) ? text : null;
    }

    public void SetDraft(ChatId chatId, string? text)
    {
        var key = Key(chatId);
        if (string.IsNullOrWhiteSpace(text))
        {
            _drafts.Remove(key);
            return;
        }

        _drafts[key] = text;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _drafts.Clear();
        if (!File.Exists(_path))
            return;

        await using var stream = File.OpenRead(_path);
        var loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (loaded is null)
            return;

        foreach (var (key, value) in loaded)
            _drafts[key] = value;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, _drafts, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(tempPath, _path, overwrite: true);
    }

    private static string Key(ChatId chatId) =>
        chatId.Value.ToString(CultureInfo.InvariantCulture);
}
