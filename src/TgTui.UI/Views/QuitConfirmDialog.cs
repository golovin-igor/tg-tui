using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TgTui.UI.Views;

/// <summary>
/// Modal confirmation before quitting when the composer has unsent state.
/// </summary>
internal static class QuitConfirmDialog
{
    public static bool Confirm(IApplication app, string message)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var confirmed = false;
        using var dialog = new Dialog
        {
            Title = "Quit tg-tui?",
            Width = 56,
            Height = 8,
            X = Pos.Center(),
            Y = Pos.Center(),
            BorderStyle = LineStyle.Single,
            SchemeName = "Dialog",
        };

        var body = new Label
        {
            Text = message,
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = 3,
            CanFocus = false,
        };

        var quit = new Button
        {
            Text = "_Quit",
            X = 1,
            Y = Pos.AnchorEnd(2),
            IsDefault = true,
        };
        quit.Accepting += (_, e) =>
        {
            confirmed = true;
            dialog.RequestStop();
            e.Handled = true;
        };

        var cancel = new Button
        {
            Text = "_Cancel",
            X = Pos.Right(quit) + 2,
            Y = Pos.AnchorEnd(2),
        };
        cancel.Accepting += (_, e) =>
        {
            dialog.RequestStop();
            e.Handled = true;
        };

        dialog.Add(body, quit, cancel);
        dialog.KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
            {
                dialog.RequestStop();
                key.Handled = true;
            }
        };

        app.Run(dialog);
        return confirmed;
    }
}
