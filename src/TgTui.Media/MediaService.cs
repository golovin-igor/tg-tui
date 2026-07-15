using System.Collections;
using System.Diagnostics;
using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.Media;

public sealed class MediaService : IMediaService
{
    private readonly MediaCache _cache;
    private readonly IMediaDownloader? _downloader;
    private readonly Func<GraphicsCapability> _getCapability;

    public MediaService(
        MediaCache cache,
        IMediaDownloader? downloader = null,
        Func<GraphicsCapability>? getCapability = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _downloader = downloader;
        _getCapability = getCapability ?? DetectFromEnvironment;
    }

    public MediaCache Cache => _cache;

    public async Task<string?> EnsureLocalAsync(MediaAttachment media, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(media.LocalPath) && File.Exists(media.LocalPath))
            return media.LocalPath;

        if (media.SourceChatId is not { } chatId || media.SourceMessageId is not { } messageId)
            return null;

        var cacheKey = CacheKey(chatId, messageId);
        if (_cache.Exists(cacheKey))
            return _cache.GetPath(cacheKey);

        if (_downloader is null)
            return null;

        var downloaded = await _downloader
            .DownloadMessageMediaAsync(chatId, messageId, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(downloaded) || !File.Exists(downloaded))
            return null;

        await using (var stream = File.OpenRead(downloaded))
            _cache.Put(cacheKey, stream);

        return _cache.GetPath(cacheKey);
    }

    public Task OpenExternallyAsync(string localPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = GetOpenCommand(localPath);
        var process = Process.Start(startInfo);
        process?.Dispose();
        return Task.CompletedTask;
    }

    public string RenderPreview(string localPath, int maxCellWidth)
    {
        var capability = _getCapability();
        return capability switch
        {
            GraphicsCapability.None => "🖼 (open with o)",
            GraphicsCapability.HalfBlock => HalfBlockImageRenderer.RenderFile(localPath, maxCellWidth),
            GraphicsCapability.Kitty or GraphicsCapability.Sixel or GraphicsCapability.ITerm2
                => ProtocolImageRenderer.RenderFile(localPath, maxCellWidth, capability),
            _ => HalfBlockImageRenderer.RenderFile(localPath, maxCellWidth)
        };
    }

    /// <summary>
    /// Builds a platform-specific process start info to open a file externally.
    /// macOS: <c>open</c>, Linux: <c>xdg-open</c>, Windows: shell execute on the path.
    /// </summary>
    public static ProcessStartInfo GetOpenCommand(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (OperatingSystem.IsMacOS())
        {
            var psi = new ProcessStartInfo
            {
                FileName = "open",
                UseShellExecute = false
            };
            psi.ArgumentList.Add(path);
            return psi;
        }

        if (OperatingSystem.IsLinux())
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xdg-open",
                UseShellExecute = false
            };
            psi.ArgumentList.Add(path);
            return psi;
        }

        // Windows and other: let the shell resolve the association
        return new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        };
    }

    internal static string CacheKey(ChatId chatId, MessageId messageId) =>
        $"{chatId.Value}_{messageId.Value}";

    private static GraphicsCapability DetectFromEnvironment()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (key is null)
                continue;
            env[key] = entry.Value?.ToString();
        }

        return new TerminalCapabilityDetector().Detect(env);
    }
}
