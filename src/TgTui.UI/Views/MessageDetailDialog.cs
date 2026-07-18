using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgTui.Core.Models;

namespace TgTui.UI.Views;

/// <summary>
/// Modal expanded message view with full text and inline media preview.
/// </summary>
internal static class MessageDetailDialog
{
    public static void Show(
        IApplication app,
        string content,
        ChatMessage message,
        Func<CancellationToken, Task>? openMediaAsync = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        using var dialog = new Dialog
        {
            Title = "Message",
            Width = Dim.Percent(75),
            Height = Dim.Percent(80),
            X = Pos.Center(),
            Y = Pos.Center(),
            BorderStyle = LineStyle.Single,
            SchemeName = "Dialog",
        };

        var body = new Label
        {
            Text = content,
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            CanFocus = false,
        };

        var close = new Button
        {
            Text = "_Close",
            X = 1,
            Y = Pos.AnchorEnd(1),
            IsDefault = true,
        };
        close.Accepting += (_, e) =>
        {
            dialog.RequestStop();
            e.Handled = true;
        };

        Button? openMedia = null;
        if (message.Media is not null && openMediaAsync is not null)
        {
            openMedia = new Button
            {
                Text = "_Open media",
                X = Pos.Right(close) + 2,
                Y = Pos.AnchorEnd(1),
            };
            openMedia.Accepting += (_, e) =>
            {
                e.Handled = true;
                _ = OpenMediaAsync(app, dialog, openMediaAsync);
            };
        }

        if (openMedia is not null)
            dialog.Add(body, close, openMedia);
        else
            dialog.Add(body, close);

        dialog.KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
            {
                dialog.RequestStop();
                key.Handled = true;
                return;
            }

            if (key == Key.O && message.Media is not null && openMediaAsync is not null)
            {
                key.Handled = true;
                _ = OpenMediaAsync(app, dialog, openMediaAsync);
            }
        };

        app.Run(dialog);
    }

    private static async Task OpenMediaAsync(
        IApplication app,
        Dialog dialog,
        Func<CancellationToken, Task> openMediaAsync)
    {
        try
        {
            await openMediaAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch
        {
            // External open must not break the detail dialog.
        }
    }
}
