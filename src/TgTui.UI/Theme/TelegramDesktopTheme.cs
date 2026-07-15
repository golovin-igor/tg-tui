using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace TgTui.UI.Theme;

/// <summary>
/// Telegram Desktop–inspired dark palette mapped to Terminal.Gui schemes.
/// </summary>
public static class TelegramDesktopTheme
{
    public static Color DeepBackground { get; } = new("#0e1621");
    public static Color PanelBackground { get; } = new("#17212b");
    public static Color Selection { get; } = new("#2b5278");
    public static Color Accent { get; } = new("#5eb5f7");
    public static Color MutedText { get; } = new("#6d7f8f");
    public static Color MutedTextAlt { get; } = new("#8b9bab");
    public static Color PrimaryText { get; } = new("#e4ecf2");

    /// <summary>Custom scheme name for selected dialog-list rows.</summary>
    public const string DialogSelectedSchemeName = "DialogSelected";

    /// <summary>Custom scheme name for incoming message bubbles.</summary>
    public const string MessageIncomingSchemeName = "MessageIncoming";

    /// <summary>Custom scheme name for outgoing message bubbles.</summary>
    public const string MessageOutgoingSchemeName = "MessageOutgoing";

    public static TgAttribute WindowNormal => new(PrimaryText, DeepBackground);
    public static TgAttribute PanelNormal => new(PrimaryText, PanelBackground);
    public static TgAttribute DialogSelected => new(PrimaryText, Selection);
    public static TgAttribute MessageIncoming => new(PrimaryText, PanelBackground);
    public static TgAttribute MessageOutgoing => new(PrimaryText, Selection);
    public static TgAttribute AccentOnDeep => new(Accent, DeepBackground);
    public static TgAttribute AccentOnPanel => new(Accent, PanelBackground);
    public static TgAttribute MutedOnDeep => new(MutedText, DeepBackground);
    public static TgAttribute MutedOnPanel => new(MutedText, PanelBackground);

    /// <summary>
    /// Registers Base / Dialog / Menu / Error / Accent and custom bubble/selection schemes.
    /// Call after <c>IApplication.Init</c>.
    /// </summary>
    public static void Apply()
    {
        SchemeManager.AddScheme(Schemes.Base.ToString(), CreateBaseScheme());
        SchemeManager.AddScheme(Schemes.Dialog.ToString(), CreateDialogScheme());
        SchemeManager.AddScheme(Schemes.Menu.ToString(), CreateMenuScheme());
        SchemeManager.AddScheme(Schemes.Error.ToString(), CreateErrorScheme());
        SchemeManager.AddScheme(Schemes.Accent.ToString(), CreateAccentScheme());

        SchemeManager.AddScheme(DialogSelectedSchemeName, CreateDialogSelectedScheme());
        SchemeManager.AddScheme(MessageIncomingSchemeName, CreateMessageIncomingScheme());
        SchemeManager.AddScheme(MessageOutgoingSchemeName, CreateMessageOutgoingScheme());
    }

    private static Scheme CreateBaseScheme() => new()
    {
        Normal = WindowNormal,
        Focus = DialogSelected,
        HotNormal = AccentOnDeep,
        HotFocus = new TgAttribute(Accent, Selection),
        Active = DialogSelected,
        HotActive = new TgAttribute(Accent, Selection),
        Highlight = new TgAttribute(PrimaryText, Selection),
        Disabled = MutedOnDeep,
        Editable = PanelNormal,
        ReadOnly = MutedOnPanel,
    };

    private static Scheme CreateDialogScheme() => new()
    {
        Normal = PanelNormal,
        Focus = DialogSelected,
        HotNormal = AccentOnPanel,
        HotFocus = new TgAttribute(Accent, Selection),
        Active = DialogSelected,
        Disabled = MutedOnPanel,
        Editable = PanelNormal,
        ReadOnly = MutedOnPanel,
    };

    private static Scheme CreateMenuScheme() => new()
    {
        Normal = PanelNormal,
        Focus = DialogSelected,
        HotNormal = AccentOnPanel,
        HotFocus = new TgAttribute(Accent, Selection),
        Active = DialogSelected,
        Disabled = MutedOnPanel,
    };

    private static Scheme CreateErrorScheme() => new()
    {
        Normal = new TgAttribute(PrimaryText, new Color("#8b2942")),
        Focus = new TgAttribute(PrimaryText, new Color("#a33a55")),
        HotNormal = new TgAttribute(Accent, new Color("#8b2942")),
        Disabled = new TgAttribute(MutedTextAlt, new Color("#8b2942")),
    };

    private static Scheme CreateAccentScheme() => new()
    {
        Normal = AccentOnPanel,
        Focus = new TgAttribute(DeepBackground, Accent),
        HotNormal = new TgAttribute(PrimaryText, Accent),
        Disabled = MutedOnPanel,
    };

    private static Scheme CreateDialogSelectedScheme() => new()
    {
        Normal = DialogSelected,
        Focus = DialogSelected,
        HotNormal = new TgAttribute(Accent, Selection),
        Active = DialogSelected,
    };

    private static Scheme CreateMessageIncomingScheme() => new()
    {
        Normal = MessageIncoming,
        Focus = MessageIncoming,
        HotNormal = AccentOnPanel,
        Disabled = MutedOnPanel,
    };

    private static Scheme CreateMessageOutgoingScheme() => new()
    {
        Normal = MessageOutgoing,
        Focus = MessageOutgoing,
        HotNormal = new TgAttribute(Accent, Selection),
        Disabled = new TgAttribute(MutedText, Selection),
    };
}
