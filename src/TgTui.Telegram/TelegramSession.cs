using WTelegram;

namespace TgTui.Telegram;

/// <summary>
/// Owns the shared WTelegram <see cref="Client"/> instance for the process lifetime.
/// Registered as a singleton in DI.
/// </summary>
public sealed class TelegramSession : IAsyncDisposable, IDisposable
{
    private Client? _client;

    public Client? Client => _client;

    public Client RequireClient() =>
        _client ?? throw new InvalidOperationException("Telegram client has not been created yet.");

    public void SetClient(Client client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (ReferenceEquals(_client, client))
            return;

        var previous = _client;
        _client = client;
        previous?.Dispose();
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }
    }
}
