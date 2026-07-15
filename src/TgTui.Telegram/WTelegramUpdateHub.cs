using System.Collections.Concurrent;
using TgTui.Core.Events;
using TgTui.Core.Ports;
using TgTui.Telegram.Mapping;
using TL;
using WTelegram;

namespace TgTui.Telegram;

public sealed class WTelegramUpdateHub : IUpdateHub
{
    private readonly TelegramSession? _session;
    private readonly TelegramPeerStore? _peers;
    private int _started;
    private Client? _subscribed;

    public WTelegramUpdateHub()
    {
    }

    public WTelegramUpdateHub(TelegramSession session, TelegramPeerStore? peers = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _peers = peers;
    }

    public event Action<DialogsChanged>? DialogsChanged;
    public event Action<MessagesChanged>? MessagesChanged;
    public event Action<ConnectionStateChanged>? ConnectionStateChanged;
    public event Action<AuthStateChanged>? AuthStateChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = _session?.Client;
        if (client is null)
            return Task.CompletedTask;

        if (Interlocked.Exchange(ref _started, 1) == 1)
            return Task.CompletedTask;

        _subscribed = client;
        client.OnUpdates += OnUpdatesAsync;
        client.OnOther += OnOtherAsync;
        Publish(new ConnectionStateChanged(IsConnected: !client.Disconnected, Detail: null));
        return Task.CompletedTask;
    }

    public void Publish(AuthStateChanged e) => AuthStateChanged?.Invoke(e);

    public void Publish(ConnectionStateChanged e) => ConnectionStateChanged?.Invoke(e);

    public void Publish(DialogsChanged e) => DialogsChanged?.Invoke(e);

    public void Publish(MessagesChanged e) => MessagesChanged?.Invoke(e);

    /// <summary>Processes a TL updates batch (exposed for unit tests without a live client).</summary>
    public void HandleUpdates(UpdatesBase updates) => ProcessUpdates(updates);

    private Task OnUpdatesAsync(UpdatesBase updates)
    {
        try
        {
            ProcessUpdates(updates);
        }
        catch
        {
            // Never let update handling tear down the MTProto reactor.
        }

        return Task.CompletedTask;
    }

    private Task OnOtherAsync(IObject obj)
    {
        try
        {
            switch (obj)
            {
                case ReactorError err:
                    Publish(new ConnectionStateChanged(false, err.Exception?.Message ?? "Reactor error"));
                    break;
                case Pong:
                    Publish(new ConnectionStateChanged(true, null));
                    break;
            }
        }
        catch
        {
            // Swallow.
        }

        return Task.CompletedTask;
    }

    private void ProcessUpdates(UpdatesBase updates)
    {
        if (updates is null)
            return;

        _peers?.Merge(updates.Users, updates.Chats);

        var messageChats = new ConcurrentDictionary<long, byte>();
        var dialogsDirty = false;

        foreach (var update in updates.UpdateList ?? Array.Empty<Update>())
        {
            // Note: UpdateNewChannelMessage : UpdateNewMessage, UpdateEditChannelMessage : UpdateEditMessage,
            // UpdateDeleteChannelMessages : UpdateDeleteMessages — match most-derived types first.
            switch (update)
            {
                case UpdateNewMessage { message: Message msg }:
                    // Includes UpdateNewChannelMessage.
                    messageChats[PeerId.FromPeer(msg.peer_id).Value] = 0;
                    dialogsDirty = true;
                    break;

                case UpdateEditMessage { message: Message msg }:
                    // Includes UpdateEditChannelMessage.
                    messageChats[PeerId.FromPeer(msg.peer_id).Value] = 0;
                    break;

                case UpdateDeleteChannelMessages delChannel:
                    messageChats[PeerId.FromChannel(delChannel.channel_id).Value] = 0;
                    dialogsDirty = true;
                    break;

                case UpdateDeleteMessages:
                    // Peer unknown for plain deletes — refresh dialog list only.
                    dialogsDirty = true;
                    break;

                case UpdateReadHistoryInbox { peer: Peer peer }:
                    messageChats[PeerId.FromPeer(peer).Value] = 0;
                    dialogsDirty = true;
                    break;

                case UpdateReadHistoryOutbox { peer: Peer peer }:
                    messageChats[PeerId.FromPeer(peer).Value] = 0;
                    break;

                case UpdateReadChannelInbox { channel_id: var channelId }:
                    messageChats[PeerId.FromChannel(channelId).Value] = 0;
                    dialogsDirty = true;
                    break;

                case UpdateDialogPinned:
                case UpdateDialogUnreadMark:
                case UpdateNotifySettings:
                case UpdateFolderPeers:
                case UpdatePinnedDialogs:
                    dialogsDirty = true;
                    break;
            }
        }

        // Short message forms arrive as UpdatesBase with synthetic UpdateList, but also surface peer via short types.
        if (updates is UpdateShortMessage usm)
        {
            messageChats[PeerId.FromUser(usm.user_id).Value] = 0;
            dialogsDirty = true;
        }
        else if (updates is UpdateShortChatMessage uscm)
        {
            messageChats[PeerId.FromChat(uscm.chat_id).Value] = 0;
            dialogsDirty = true;
        }

        foreach (var chatValue in messageChats.Keys)
            Publish(new MessagesChanged(new Core.Models.ChatId(chatValue)));

        if (dialogsDirty)
            Publish(new DialogsChanged());
    }
}
