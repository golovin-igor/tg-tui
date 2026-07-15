using TgTui.Core.Models;
using TgTui.Core.Ports;

namespace TgTui.UI.Fakes;

/// <summary>
/// Offline/fallback media port: keeps UI free of <c>TgTui.Media</c>.
/// Returns existing local paths when present; previews are placeholders only.
/// </summary>
public sealed class FakeMediaService : IMediaService
{
    public Task<string?> EnsureLocalAsync(MediaAttachment media, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(media.LocalPath) && File.Exists(media.LocalPath))
            return Task.FromResult<string?>(media.LocalPath);

        return Task.FromResult<string?>(null);
    }

    public Task OpenExternallyAsync(string localPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public string RenderPreview(string localPath, int maxCellWidth) => "🖼 (open with o)";
}
