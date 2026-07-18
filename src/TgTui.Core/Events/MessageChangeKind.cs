namespace TgTui.Core.Events;

/// <summary>How a <see cref="MessagesChanged"/> event should be applied in the UI.</summary>
public enum MessageChangeKind
{
    /// <summary>Full history reload (fallback when the update payload is incomplete).</summary>
    Refresh,

    /// <summary>A new message arrived.</summary>
    Added,

    /// <summary>An existing message was edited.</summary>
    Edited,

    /// <summary>One or more messages were deleted.</summary>
    Deleted,

    /// <summary>Read receipts changed for this chat.</summary>
    ReadStateChanged,
}
