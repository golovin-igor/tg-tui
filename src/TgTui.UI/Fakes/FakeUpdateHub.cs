using TgTui.Core.Events;
using TgTui.Core.Ports;

namespace TgTui.UI.Fakes;

/// <summary>
/// No-op update hub for offline UI (<c>TG_TUI_FAKE=1</c>); reports connected on start.
/// </summary>
public sealed class FakeUpdateHub : IUpdateHub
{
    public event Action<DialogsChanged>? DialogsChanged;
    public event Action<MessagesChanged>? MessagesChanged;
    public event Action<ConnectionStateChanged>? ConnectionStateChanged;
    public event Action<AuthStateChanged>? AuthStateChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectionStateChanged?.Invoke(new ConnectionStateChanged(true, "fake"));
        return Task.CompletedTask;
    }

    // Keep event fields "used" for analyzers when nothing subscribes in pure unit tests.
    internal void PublishDialogsChanged() => DialogsChanged?.Invoke(new DialogsChanged());
    internal void PublishMessagesChanged(Core.Models.ChatId chatId) =>
        MessagesChanged?.Invoke(new MessagesChanged(chatId));
    internal void PublishAuth(AuthStateChanged e) => AuthStateChanged?.Invoke(e);
}
