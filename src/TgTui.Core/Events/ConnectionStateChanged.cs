namespace TgTui.Core.Events;

public sealed record ConnectionStateChanged(bool IsConnected, string? Detail);
