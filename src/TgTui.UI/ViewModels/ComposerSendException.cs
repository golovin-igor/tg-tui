using TgTui.Core.Models;

namespace TgTui.UI.ViewModels;

/// <summary>
/// Raised when an optimistic send fails after the temporary message was presented.
/// </summary>
public sealed class ComposerSendException : Exception
{
    public ComposerSendException(MessageId optimisticId, string message)
        : base(message)
    {
        OptimisticId = optimisticId;
    }

    public ComposerSendException(MessageId optimisticId, string message, Exception innerException)
        : base(message, innerException)
    {
        OptimisticId = optimisticId;
    }

    public MessageId OptimisticId { get; }
}
