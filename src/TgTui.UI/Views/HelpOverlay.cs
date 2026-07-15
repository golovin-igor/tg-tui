using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using AppKeys = TgTui.UI.Keymap.KeyBindings;

namespace TgTui.UI.Views;

/// <summary>
/// Modal help overlay listing default key bindings (design §3.4).
/// </summary>
public sealed class HelpOverlay : Dialog
{
    public HelpOverlay()
    {
        Title = "Help — Keyboard shortcuts";
        Width = Dim.Percent(80);
        Height = Dim.Percent(85);
        X = Pos.Center();
        Y = Pos.Center();
        SchemeName = "Dialog";

        var body = new Label
        {
            Text = AppKeys.BuildHelpText(),
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            CanFocus = false,
        };
        Add(body);

        var close = new Button
        {
            Text = "_Close",
            IsDefault = true,
        };
        close.Accepting += (_, e) =>
        {
            RequestStop();
            e.Handled = true;
        };
        AddButton(close);

        AddCommand(Command.Quit, () =>
        {
            RequestStop();
            return true;
        });
        base.KeyBindings.Add(Key.Esc, Command.Quit);
    }

    /// <summary>Runs the help dialog as a modal session on <paramref name="app"/>.</summary>
    public static void Show(IApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        using var dialog = new HelpOverlay();
        app.Run(dialog);
    }
}
