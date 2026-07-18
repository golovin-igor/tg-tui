using Microsoft.Extensions.Logging;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgTui.Core.Ports;
using TgTui.UI.Theme;
using TgTui.UI.Views;
using AppKeys = TgTui.UI.Keymap.KeyBindings;

namespace TgTui.UI;

/// <summary>
/// Boots Terminal.Gui, runs the auth wizard (unless skipped), then shows the chat shell.
/// </summary>
public static class AppRunner
{
    private static ChatShellView? _activeShell;

    /// <summary>
    /// Runs the TUI. When <paramref name="skipAuth"/> is true (offline fake mode),
    /// the chat shell is shown immediately.
    /// </summary>
    public static void Run(
        ChatShellDependencies shellDependencies,
        IAuthService? authService = null,
        bool skipAuth = false,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(shellDependencies);

        using IApplication app = Application.Create();
        app.Init();
        TelegramDesktopTheme.Apply();

        using var window = CreateMainWindow(app, shellDependencies, authService, skipAuth, logger);
        app.Run(window);
    }

    /// <summary>Backward-compatible entry used by older call sites / tests.</summary>
    public static void Run(IAuthService authService, IUpdateHub? updateHub = null) =>
        Run(authService, updateHub, logger: null);

    /// <summary>Backward-compatible entry used by older call sites / tests.</summary>
    public static void Run(IAuthService authService, IUpdateHub? updateHub, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(authService);
        // Minimal shell deps when only auth is provided (placeholder path no longer used).
        var drafts = new Core.Drafts.FileDraftStore(Path.Combine(Path.GetTempPath(), "tg-tui-drafts-fallback.json"));
        var shell = new ChatShellDependencies(
            Dialogs: new Fakes.FakeDialogService(),
            Messages: new Fakes.FakeMessageService(),
            Drafts: drafts,
            Media: new Fakes.FakeMediaService(),
            Updates: updateHub ?? new Fakes.FakeUpdateHub());
        Run(shell, authService, skipAuth: false, logger: logger);
    }

    private static Window CreateMainWindow(
        IApplication app,
        ChatShellDependencies shellDependencies,
        IAuthService? authService,
        bool skipAuth,
        ILogger? logger)
    {
        var window = new Window
        {
            Title = "tg-tui",
            BorderStyle = LineStyle.Single,
            SchemeName = Schemes.Base.ToString(),
        };

        AttachGlobalKeys(app, window, logger);

        if (skipAuth || authService is null)
        {
            ShowChatShell(window, app, shellDependencies, logger);
            return window;
        }

        AuthWizardView? wizard = null;
        wizard = new AuthWizardView(
            authService,
            app,
            onReady: () => ShowChatShell(window, app, shellDependencies, logger, wizard),
            hub: shellDependencies.Updates);

        window.Add(wizard);

        app.AddTimeout(TimeSpan.Zero, () =>
        {
            _ = wizard.StartAsync();
            return false;
        });

        return window;
    }

    private static void ShowChatShell(
        Window window,
        IApplication app,
        ChatShellDependencies shellDependencies,
        ILogger? logger,
        View? wizard = null)
    {
        if (wizard is not null)
        {
            window.Remove(wizard);
            wizard.Dispose();
        }

        // Clear any leftover children (e.g. if called twice).
        var existing = window.SubViews.ToArray();
        foreach (var child in existing)
        {
            window.Remove(child);
            child.Dispose();
        }

        var shell = new ChatShellView(app, shellDependencies, logger)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        _activeShell = shell;

        window.Title = "tg-tui";
        window.Add(shell);

        app.AddTimeout(TimeSpan.Zero, () =>
        {
            _ = shell.StartAsync();
            return false;
        });
    }

    private static void AttachGlobalKeys(IApplication app, Window window, ILogger? logger)
    {
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
                key.Handled = true;
                _ = TryQuitAsync(app, logger);
            }
        };
    }

    private static async Task TryQuitAsync(IApplication app, ILogger? logger)
    {
        try
        {
            if (_activeShell is not null)
            {
                if (!_activeShell.TryConfirmQuit(app))
                    return;

                await _activeShell.FlushDraftAsync().ConfigureAwait(true);
            }

            app.RequestStop();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Quit flow failed");
            app.RequestStop();
        }
    }

    private static bool IsHelpKey(Key key) =>
        Key.TryParse(AppKeys.GlobalHelp, out var help) && key == help;

    private static bool IsQuitKey(Key key) =>
        Key.TryParse(AppKeys.GlobalQuit, out var quit) && key == quit;
}
