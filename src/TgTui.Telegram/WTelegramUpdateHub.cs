using TgTui.Core.Events;
using TgTui.Core.Models;
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

        var dialogsDirty = false;

        foreach (var update in updates.UpdateList ?? Array.Empty<Update>())
        {
            // Note: UpdateNewChannelMessage : UpdateNewMessage, UpdateEditChannelMessage : UpdateEditMessage,
            // UpdateDeleteChannelMessages : UpdateDeleteMessages — match most-derived types first.
            switch (update)
            {
                case UpdateNewMessage { message: Message msg }:
                    PublishMappedMessage(msg, MessageChangeKind.Added);
                    dialogsDirty = true;
                    break;

                case UpdateEditMessage { message: Message msg }:
                    PublishMappedMessage(msg, MessageChangeKind.Edited);
                    break;

                case UpdateDeleteChannelMessages delChannel:
                    PublishDeleted(
                        PeerId.FromChannel(delChannel.channel_id),
                        delChannel.messages.Select(static id => new MessageId(id)).ToArray());
                    dialogsDirty = true;
                    break;

                case UpdateDeleteMessages:
                    // Peer unknown for plain deletes — refresh dialog list only.
                    dialogsDirty = true;
                    break;

                case UpdateReadHistoryInbox { peer: Peer peer, max_id: var maxId }:
                    PublishReadState(PeerId.FromPeer(peer), maxId, outboxMaxId: null);
                    dialogsDirty = true;
                    break;

                case UpdateReadHistoryOutbox { peer: Peer peer, max_id: var maxId }:
                    PublishReadState(PeerId.FromPeer(peer), inboxMaxId: null, outboxMaxId: maxId);
                    break;

                case UpdateReadChannelInbox { channel_id: var channelId, max_id: var maxId }:
                    PublishReadState(PeerId.FromChannel(channelId), maxId, outboxMaxId: null);
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

        if (updates is UpdateShortMessage usm)
        {
            PublishMappedMessage(
                new Message
                {
                    id = usm.id,
                    message = usm.message,
                    peer_id = new PeerUser { user_id = usm.user_id },
                    date = usm.date,
                    flags = (Message.Flags)usm.flags,
                },
                MessageChangeKind.Added);
            dialogsDirty = true;
        }
        else if (updates is UpdateShortChatMessage uscm)
        {
            PublishMappedMessage(
                new Message
                {
                    id = uscm.id,
                    message = uscm.message,
                    peer_id = new PeerChat { chat_id = uscm.chat_id },
                    date = uscm.date,
                    flags = (Message.Flags)uscm.flags,
                },
                MessageChangeKind.Added);
            dialogsDirty = true;
        }

        if (dialogsDirty)
            Publish(new DialogsChanged());
    }

    private void PublishMappedMessage(Message msg, MessageChangeKind kind)
    {
        if (msg.peer_id is null)
            return;

        var chatId = PeerId.FromPeer(msg.peer_id);
        var mapped = MapMessage(msg, chatId);
        Publish(new MessagesChanged(chatId, kind, mapped));
    }

    private void PublishDeleted(ChatId chatId, IReadOnlyList<MessageId> deletedIds)
    {
        if (deletedIds.Count == 0)
            return;

        Publish(new MessagesChanged(chatId, MessageChangeKind.Deleted, DeletedIds: deletedIds));
    }

    private void PublishReadState(ChatId chatId, int? inboxMaxId, int? outboxMaxId)
    {
        var curInbox = 0;
        var curOutbox = 0;
        _peers?.TryGetReadMarkers(chatId, out curInbox, out curOutbox);
        var inbox = inboxMaxId ?? curInbox;
        var outbox = outboxMaxId ?? curOutbox;
        if (inboxMaxId is not null || outboxMaxId is not null)
            _peers?.SetReadMarkers(chatId, inbox, outbox);

        Publish(new MessagesChanged(
            chatId,
            MessageChangeKind.ReadStateChanged,
            ReadInboxMaxId: inboxMaxId ?? inbox,
            ReadOutboxMaxId: outboxMaxId ?? outbox));
    }

    private ChatMessage MapMessage(Message msg, ChatId chatId)
    {
        int? readInbox = null;
        int? readOutbox = null;
        if (_peers?.TryGetReadMarkers(chatId, out var inboxMax, out var outboxMax) == true)
        {
            readInbox = inboxMax;
            readOutbox = outboxMax;
        }

        return TelegramMapper.MapMessage(msg, chatId, readInbox, readOutbox);
    }
}
