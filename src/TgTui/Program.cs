using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TgTui.Core.Config;
using TgTui.Core.Drafts;
using TgTui.Core.Paths;
using TgTui.Core.Ports;
using TgTui.Media;
using TgTui.Telegram;
using TgTui.UI;
using TgTui.UI.Fakes;

var useFake = string.Equals(
    Environment.GetEnvironmentVariable("TG_TUI_FAKE"),
    "1",
    StringComparison.Ordinal);

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(AppPaths.ForCurrentUser());
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
    // Offline shell: placeholders only (no Telegram download / ImageSharp path).
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

using var host = builder.Build();

var paths = host.Services.GetRequiredService<AppPaths>();
paths.EnsureCreated();

var shell = new ChatShellDependencies(
    Dialogs: host.Services.GetRequiredService<IDialogService>(),
    Messages: host.Services.GetRequiredService<IMessageService>(),
    Drafts: host.Services.GetRequiredService<IDraftStore>(),
    Media: host.Services.GetRequiredService<IMediaService>(),
    Updates: host.Services.GetRequiredService<IUpdateHub>());

if (useFake)
{
    AppRunner.Run(shell, authService: null, skipAuth: true);
}
else
{
    var auth = host.Services.GetRequiredService<IAuthService>();
    AppRunner.Run(shell, auth, skipAuth: false);
}
