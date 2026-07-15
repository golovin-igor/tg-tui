using System.Diagnostics;
using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.Media;

public sealed class MediaService : IMediaService
{
    private readonly MediaCache _cache;
    private readonly IMediaDownloader? _downloader;

    public MediaService(MediaCache cache, IMediaDownloader? downloader = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _downloader = downloader;
    }

    public MediaCache Cache => _cache;

    public async Task<string?> EnsureLocalAsync(MediaAttachment media, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(media.LocalPath) && File.Exists(media.LocalPath))
            return media.LocalPath;

        if (_downloader is not null
            && media.SourceChatId is { } chatId
            && media.SourceMessageId is { } messageId)
        {
            var downloaded = await _downloader
                .DownloadMessageMediaAsync(chatId, messageId, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(downloaded) ? null : downloaded;
        }

        return null;
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

    public string RenderPreview(string localPath, int maxCellWidth) =>
        HalfBlockImageRenderer.RenderFile(localPath, maxCellWidth);

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
}
