using TgTui.Core.Paths;
using WTelegram;

namespace TgTui.Telegram;

public static class TelegramClientFactory
{
    /// <summary>
    /// Creates a WTelegram client. The config callback is queried for:
    /// <c>api_id</c>, <c>api_hash</c>, <c>phone_number</c>, <c>verification_code</c>,
    /// <c>password</c>, <c>session_pathname</c>, and other keys WTelegram may request.
    /// </summary>
    public static Client Create(AppPaths paths, Func<string, string?> config)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(config);

        return new Client(what =>
        {
            if (what == "session_pathname")
                return paths.SessionFile;

            return config(what);
        });
    }
}
