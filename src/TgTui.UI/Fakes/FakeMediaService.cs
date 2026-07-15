using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.UI.Fakes;

/// <summary>
/// Offline/fallback media port: keeps UI free of <c>TgTui.Media</c>.
/// Returns existing local paths when present; otherwise a stable fake path so the
/// message pane can still show inline placeholders without downloading.
/// </summary>
public sealed class FakeMediaService : IMediaService
{
    /// <summary>Synthetic path returned when no real local file exists (offline demo).</summary>
    public const string PlaceholderPath = "fake-media-placeholder";

    /// <summary>Inline preview text used when no real graphics pipeline is available.</summary>
    public const string PlaceholderPreview = "🖼 (open with o)";

    /// <summary>Paths passed to <see cref="OpenExternallyAsync"/> (tests / diagnostics).</summary>
    public IReadOnlyList<string> OpenedPaths => _openedPaths;

    private readonly List<string> _openedPaths = [];

    public Task<string?> EnsureLocalAsync(MediaAttachment media, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(media.LocalPath) && File.Exists(media.LocalPath))
            return Task.FromResult<string?>(media.LocalPath);

        // Offline: pretend media is ready so the pane shows a placeholder instead of "unavailable".
        return Task.FromResult<string?>(PlaceholderPath);
    }

    public Task OpenExternallyAsync(string localPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        cancellationToken.ThrowIfCancellationRequested();
        _openedPaths.Add(localPath);
        return Task.CompletedTask;
    }

    public string RenderPreview(string localPath, int maxCellWidth)
    {
        // Always a compact one-line hint — no SkiaSharp / protocol dependency in UI.
        _ = localPath;
        _ = maxCellWidth;
        return PlaceholderPreview;
    }
}
