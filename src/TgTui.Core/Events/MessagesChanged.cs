using TgTui.Core.Models;

namespace TgTui.Core.Events;

public sealed record MessagesChanged(
    ChatId ChatId,
    MessageChangeKind Kind = MessageChangeKind.Refresh,
    ChatMessage? Message = null,
    IReadOnlyList<MessageId>? DeletedIds = null,
    int? ReadInboxMaxId = null,
    int? ReadOutboxMaxId = null);
