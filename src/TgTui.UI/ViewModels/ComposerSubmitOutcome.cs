using TgTui.Core.Models;

namespace TgTui.UI.ViewModels;

/// <summary>
/// Result of a successful composer submit (send or edit).
/// For sends, <see cref="OptimisticMessage"/> is presented before the network returns
/// and <see cref="ConfirmedMessage"/> replaces it when the server acknowledges.
/// </summary>
public sealed class ComposerSubmitOutcome
{
    public required bool IsEdit { get; init; }

    /// <summary>Temporary outgoing message shown immediately (send only).</summary>
    public ChatMessage? OptimisticMessage { get; init; }

    /// <summary>Server-confirmed message after send (send only).</summary>
    public ChatMessage? ConfirmedMessage { get; init; }

    /// <summary>Edited message id when <see cref="IsEdit"/> is true.</summary>
    public MessageId? EditedMessageId { get; init; }

    /// <summary>New text after a successful edit.</summary>
    public string? EditedText { get; init; }
}
