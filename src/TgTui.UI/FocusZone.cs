namespace TgTui.UI;

/// <summary>Which pane receives keyboard input in the chat shell (design §3.3).</summary>
public enum FocusZone
{
    Dialogs,
    Messages,
    Composer,
}
