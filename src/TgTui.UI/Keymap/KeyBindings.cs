using System.Text;

namespace TgTui.UI.Keymap;

/// <summary>
/// Display strings for keyboard shortcuts (design §3.4).
/// Actual Terminal.Gui bindings live on views; these constants drive help text and chrome hints.
/// </summary>
public static class KeyBindings
{
    // --- Dialog list ---
    public const string DialogMove = "j / k or arrows";
    public const string DialogMoveAction = "Move selection";
    public const string DialogOpen = "Enter / l";
    public const string DialogOpenAction = "Open chat";
    public const string DialogFilter = "/";
    public const string DialogFilterAction = "Filter dialogs";
    public const string DialogMute = "m";
    public const string DialogMuteAction = "Mute / unmute";
    public const string DialogPin = "p";
    public const string DialogPinAction = "Pin / unpin";

    // --- Message pane ---
    public const string MessageMove = "j / k";
    public const string MessageMoveAction = "Select message";
    public const string MessageReply = "r";
    public const string MessageReplyAction = "Reply";
    public const string MessageEdit = "e";
    public const string MessageEditAction = "Edit (own messages)";
    public const string MessageDelete = "d";
    public const string MessageDeleteAction = "Delete";
    public const string MessageOpenMedia = "o";
    public const string MessageOpenMediaAction = "Open media externally";
    public const string MessageExpand = "Enter";
    public const string MessageExpandAction = "Expand / open media when on media message";
    public const string MessageFocusComposer = "i / a";
    public const string MessageFocusComposerAction = "Focus composer";
    public const string MessageFocusDialogs = "g / h";
    public const string MessageFocusDialogsAction = "Focus dialog list";
    public const string MessageJumpLatest = "G";
    public const string MessageJumpLatestAction = "Jump to latest";

    // --- Composer ---
    public const string ComposerSend = "Enter";
    public const string ComposerSendAction = "Send";
    public const string ComposerNewline = "Shift+Enter";
    public const string ComposerNewlineAction = "Newline";
    public const string ComposerCancel = "Esc";
    public const string ComposerCancelAction = "Cancel reply / leave composer";

    // --- Global ---
    public const string GlobalHelp = "?";
    public const string GlobalHelpAction = "Help overlay";
    public const string GlobalRedraw = "Ctrl+L";
    public const string GlobalRedrawAction = "Redraw";
    public const string GlobalQuit = "q";
    public const string GlobalQuitAction = "Quit (confirm if needed)";

    /// <summary>Compact status-chrome hint.</summary>
    public const string StatusHint = "? help · q quit · Ctrl+L redraw";

    /// <summary>Full multi-line help tables matching design §3.4.</summary>
    public static string BuildHelpText()
    {
        var sb = new StringBuilder();
        AppendSection(sb, "Dialog list",
            (DialogMove, DialogMoveAction),
            (DialogOpen, DialogOpenAction),
            (DialogFilter, DialogFilterAction),
            (DialogMute, DialogMuteAction),
            (DialogPin, DialogPinAction));

        AppendSection(sb, "Message pane",
            (MessageMove, MessageMoveAction),
            (MessageReply, MessageReplyAction),
            (MessageEdit, MessageEditAction),
            (MessageDelete, MessageDeleteAction),
            (MessageOpenMedia, MessageOpenMediaAction),
            (MessageExpand, MessageExpandAction),
            (MessageFocusComposer, MessageFocusComposerAction),
            (MessageFocusDialogs, MessageFocusDialogsAction),
            (MessageJumpLatest, MessageJumpLatestAction));

        AppendSection(sb, "Composer",
            (ComposerSend, ComposerSendAction),
            (ComposerNewline, ComposerNewlineAction),
            (ComposerCancel, ComposerCancelAction));

        AppendSection(sb, "Global",
            (GlobalHelp, GlobalHelpAction),
            (GlobalRedraw, GlobalRedrawAction),
            (GlobalQuit, GlobalQuitAction));

        return sb.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder sb, string title, params (string Key, string Action)[] rows)
    {
        sb.AppendLine(title);
        sb.AppendLine(new string('─', title.Length));
        foreach (var (key, action) in rows)
        {
            sb.Append("  ");
            sb.Append(key.PadRight(28));
            sb.AppendLine(action);
        }

        sb.AppendLine();
    }
}
