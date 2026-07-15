using TgTui.Core.Models;

namespace TgTui.Core.Ports;

public interface IMediaService
{
    Task<string?> EnsureLocalAsync(MediaAttachment media, CancellationToken cancellationToken = default);
    Task OpenExternallyAsync(string localPath, CancellationToken cancellationToken = default);
    string RenderPreview(string localPath, int maxCellWidth);
}
