using TgTui.Core.Events;

namespace TgTui.Core.Ports;

public interface IUpdateHub
{
    event Action<DialogsChanged>? DialogsChanged;
    event Action<MessagesChanged>? MessagesChanged;
    event Action<ConnectionStateChanged>? ConnectionStateChanged;
    event Action<AuthStateChanged>? AuthStateChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
}
