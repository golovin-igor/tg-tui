using TgTui.Core.Models;
using TgTui.Core.Ports;
using TgTui.Telegram.Mapping;
using TL;
using WTelegram;

namespace TgTui.Telegram;

public sealed class WTelegramDialogService : IDialogService
{
    private static readonly DateTime MuteForever = new(2038, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TelegramSession _session;
    private readonly TelegramPeerStore _peers;

    public WTelegramDialogService(TelegramSession session, TelegramPeerStore peers)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
    }

    public async Task<IReadOnlyList<DialogItem>> GetDialogsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = _session.RequireClient();

        var dialogs = await client.Messages_GetAllDialogs().ConfigureAwait(false);
        _peers.Merge(dialogs);

        var topByPeer = new Dictionary<long, MessageBase>();
        foreach (var msg in dialogs.messages)
        {
            if (msg.Peer is null)
                continue;
            // Prefer exact top-message match later; index latest seen per peer id (raw).
            var key = msg.Peer.ID;
            if (!topByPeer.TryGetValue(key, out var existing) || msg.ID > existing.ID)
                topByPeer[key] = msg;
        }

        var selfId = client.User?.id;
        var items = new List<DialogItem>();

        foreach (var baseDialog in dialogs.dialogs)
        {
            if (baseDialog is not Dialog dialog)
                continue;

            var peerInfo = dialogs.UserOrChat(dialog);
            MessageBase? top = null;
            if (topByPeer.TryGetValue(dialog.Peer.ID, out var candidate)
                && candidate.ID == dialog.TopMessage)
            {
                top = candidate;
            }
            else
            {
                top = dialogs.messages.FirstOrDefault(m =>
                    m.Peer is not null
                    && m.Peer.ID == dialog.Peer.ID
                    && m.ID == dialog.TopMessage);
            }

            items.Add(TelegramMapper.MapDialog(dialog, peerInfo, top, selfId));
        }

        return TelegramMapper.SortDialogs(items);
    }

    public async Task SetMutedAsync(ChatId chatId, bool muted, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = _session.RequireClient();
        var peer = _peers.Require(chatId);

        var settings = new InputPeerNotifySettings
        {
            flags = InputPeerNotifySettings.Flags.has_mute_until,
            mute_until = muted ? MuteForever : default
        };

        await client.Account_UpdateNotifySettings(
            new InputNotifyPeer { peer = peer },
            settings).ConfigureAwait(false);
    }

    public async Task SetPinnedAsync(ChatId chatId, bool pinned, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = _session.RequireClient();
        var peer = _peers.Require(chatId);

        await client.Messages_ToggleDialogPin(
            new InputDialogPeer { peer = peer },
            pinned).ConfigureAwait(false);
    }
}
