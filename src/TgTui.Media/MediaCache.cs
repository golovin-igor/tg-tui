using System.Security.Cryptography;
using System.Text;

namespace TgTui.Media;

public sealed class MediaCache
{
    private readonly string _root;

    public MediaCache(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public string GetPath(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Path.Combine(_root, SanitizeKey(key));
    }

    public bool Exists(string key) => File.Exists(GetPath(key));

    public void Put(string key, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = GetPath(key);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        using (var file = File.Create(tempPath))
            content.CopyTo(file);

        File.Move(tempPath, path, overwrite: true);
    }

    private static string SanitizeKey(string key)
    {
        // Keep short alphanumeric-ish keys as-is; otherwise hash for safe file names.
        var safe = true;
        foreach (var c in key)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
                continue;
            safe = false;
            break;
        }

        if (safe && key.Length <= 128)
            return key;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
