using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgTui.UI.Theme;
using TgTui.UI.Views;
using AppKeys = TgTui.UI.Keymap.KeyBindings;

namespace TgTui.UI;

/// <summary>
/// Boots Terminal.Gui, applies the default theme, and shows the shell placeholder.
/// </summary>
public static class AppRunner
{
    public static void Run()
    {
        using IApplication app = Application.Create();
        app.Init();
        TelegramDesktopTheme.Apply();

        using var window = CreateMainWindow(app);
        app.Run(window);
    }

    private static Window CreateMainWindow(IApplication app)
    {
        var window = new Window
        {
            Title = "tg-tui",
            BorderStyle = LineStyle.Single,
            SchemeName = Schemes.Base.ToString(),
        };

        var title = new Label
        {
            Text = "tg-tui",
            X = Pos.Center(),
            Y = Pos.Center() - 1,
            CanFocus = false,
        };

        var hint = new Label
        {
            Text = AppKeys.StatusHint,
            X = Pos.Center(),
            Y = Pos.Center() + 1,
            CanFocus = false,
        };

        window.Add(title, hint);

        // Global shortcuts from design §3.4 — help / redraw / quit.
        window.KeyDown += (_, key) =>
        {
            if (IsHelpKey(key))
            {
                HelpOverlay.Show(app);
                key.Handled = true;
                return;
            }

            if (key == Key.L.WithCtrl)
            {
                app.LayoutAndDraw(true);
                key.Handled = true;
                return;
            }

            if (IsQuitKey(key))
            {
                app.RequestStop();
                key.Handled = true;
            }
        };

        return window;
    }

    private static bool IsHelpKey(Key key) =>
        Key.TryParse(AppKeys.GlobalHelp, out var help) && key == help;

    private static bool IsQuitKey(Key key) =>
        Key.TryParse(AppKeys.GlobalQuit, out var quit) && key == quit;
}
