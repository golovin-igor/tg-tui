using TgTui.Core.Models;

namespace TgTui.Core.Ports;

public interface IMessageService
{
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(ChatId chatId, MessageId? beforeId, int limit, CancellationToken cancellationToken = default);
    Task<ChatMessage> SendTextAsync(ChatId chatId, string text, MessageId? replyToId, CancellationToken cancellationToken = default);
    Task EditTextAsync(ChatId chatId, MessageId messageId, string text, CancellationToken cancellationToken = default);
    Task DeleteAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken = default);
}
