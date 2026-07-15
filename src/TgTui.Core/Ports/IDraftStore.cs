using TgTui.Core.Models;

namespace TgTui.Core.Ports;

public interface IDraftStore
{
    string? GetDraft(ChatId chatId);
    void SetDraft(ChatId chatId, string? text);
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}
