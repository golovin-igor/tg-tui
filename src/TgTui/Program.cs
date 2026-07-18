using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TgTui.Core.Config;
using TgTui.Core.Drafts;
using TgTui.Core.Paths;
using TgTui.Core.Ports;
using TgTui.Logging;
using TgTui.Media;
using TgTui.Telegram;
using TgTui.UI;
using TgTui.UI.Fakes;

var useFake = string.Equals(
    Environment.GetEnvironmentVariable("TG_TUI_FAKE"),
    "1",
    StringComparison.Ordinal);

var builder = Host.CreateApplicationBuilder(args);

var appPaths = AppPaths.ForCurrentUser();
appPaths.EnsureCreated();

builder.Services.AddSingleton(appPaths);
builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<IDraftStore>(sp =>
    new FileDraftStore(sp.GetRequiredService<AppPaths>().DraftsFile));

if (useFake)
{
    // Offline shell: no WTelegram / auth. skipAuth opens ChatShellView immediately.
    builder.Services.AddSingleton<IDialogService, FakeDialogService>();
    builder.Services.AddSingleton<IMessageService, FakeMessageService>();
    builder.Services.AddSingleton<IUpdateHub, FakeUpdateHub>();
}
else
{
    builder.Services.AddSingleton<TelegramSession>();
    builder.Services.AddSingleton<TelegramPeerStore>();
    builder.Services.AddSingleton<WTelegramUpdateHub>(sp =>
        new WTelegramUpdateHub(
            sp.GetRequiredService<TelegramSession>(),
            sp.GetRequiredService<TelegramPeerStore>()));
    builder.Services.AddSingleton<IUpdateHub>(sp => sp.GetRequiredService<WTelegramUpdateHub>());
    builder.Services.AddSingleton<IAuthService, WTelegramAuthService>();
    builder.Services.AddSingleton<IDialogService, WTelegramDialogService>();
    builder.Services.AddSingleton<IMessageService, WTelegramMessageService>();
}

if (useFake)
{
    // Offline shell: placeholders only (no Telegram download / SkiaSharp path).
    builder.Services.AddSingleton<IMediaService, FakeMediaService>();
}
else
{
    builder.Services.AddSingleton<IMediaDownloader>(sp =>
        new WTelegramMediaDownloader(
            sp.GetRequiredService<TelegramSession>(),
            sp.GetRequiredService<TelegramPeerStore>(),
            sp.GetRequiredService<AppPaths>().MediaCacheDir));
    builder.Services.AddSingleton(sp =>
        new MediaCache(sp.GetRequiredService<AppPaths>().MediaCacheDir));
    builder.Services.AddSingleton<IMediaService>(sp =>
        new MediaService(
            sp.GetRequiredService<MediaCache>(),
            sp.GetRequiredService<IMediaDownloader>()));
}

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddConsole();
builder.Logging.AddProvider(new FileLoggerProvider(appPaths.LogsDir));

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TgTui");

var shell = new ChatShellDependencies(
    Dialogs: host.Services.GetRequiredService<IDialogService>(),
    Messages: host.Services.GetRequiredService<IMessageService>(),
    Drafts: host.Services.GetRequiredService<IDraftStore>(),
    Media: host.Services.GetRequiredService<IMediaService>(),
    Updates: host.Services.GetRequiredService<IUpdateHub>());

try
{
    if (useFake)
    {
        AppRunner.Run(shell, authService: null, skipAuth: true, logger: logger);
    }
    else
    {
        var auth = host.Services.GetRequiredService<IAuthService>();
        AppRunner.Run(shell, auth, skipAuth: false, logger: logger);
    }
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Unhandled error in tg-tui");
    throw;
}
finally
{
    logger.LogInformation("tg-tui shutting down");
}
