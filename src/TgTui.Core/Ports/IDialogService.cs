using TgTui.Core.Models;

namespace TgTui.Core.Ports;

public interface IDialogService
{
    Task<IReadOnlyList<DialogItem>> GetDialogsAsync(CancellationToken cancellationToken = default);
    Task SetMutedAsync(ChatId chatId, bool muted, CancellationToken cancellationToken = default);
    Task SetPinnedAsync(ChatId chatId, bool pinned, CancellationToken cancellationToken = default);
}
