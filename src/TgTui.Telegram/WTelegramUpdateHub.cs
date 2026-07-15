using TgTui.Core.Events;
using TgTui.Core.Ports;

namespace TgTui.Telegram;

public sealed class WTelegramUpdateHub : IUpdateHub
{
    public event Action<DialogsChanged>? DialogsChanged;
    public event Action<MessagesChanged>? MessagesChanged;
    public event Action<ConnectionStateChanged>? ConnectionStateChanged;
    public event Action<AuthStateChanged>? AuthStateChanged;

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void Publish(AuthStateChanged e) => AuthStateChanged?.Invoke(e);

    public void Publish(ConnectionStateChanged e) => ConnectionStateChanged?.Invoke(e);

    public void Publish(DialogsChanged e) => DialogsChanged?.Invoke(e);

    public void Publish(MessagesChanged e) => MessagesChanged?.Invoke(e);
}
