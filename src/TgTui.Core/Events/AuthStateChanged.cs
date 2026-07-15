using TgTui.Core.Models;

namespace TgTui.Core.Events;

public sealed record AuthStateChanged(AuthState State);
