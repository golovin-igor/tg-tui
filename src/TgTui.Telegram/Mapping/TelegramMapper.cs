using TgTui.Core.Models;
using TL;

namespace TgTui.Telegram.Mapping;

/// <summary>
/// Pure mapping helpers from Telegram TL types / simple inputs to domain models.
/// No network I/O.
/// </summary>
public static class TelegramMapper
{
    public const string DeletedAccountTitle = "Deleted account";
    public const string SavedMessagesTitle = "Saved Messages";
    public const string PhotoPreview = "📷 Photo";
    public const string FilePreview = "📎 File";
    public const string StickerPreview = "Sticker";

    /// <summary>
    /// Builds a dialog list preview: non-empty text wins; otherwise media placeholders.
    /// Pass <paramref name="documentName"/> as <c>"sticker"</c> (case-insensitive) for stickers.
    /// </summary>
    public static string Preview(string? text, bool hasPhoto, bool hasDocument, string? documentName)
    {
        if (!string.IsNullOrEmpty(text))
            return text;

        if (hasPhoto)
            return PhotoPreview;

        if (hasDocument)
        {
            if (string.Equals(documentName, "sticker", StringComparison.OrdinalIgnoreCase))
                return StickerPreview;
            return FilePreview;
        }

        return string.Empty;
    }

    /// <summary>First character of <paramref name="title"/> uppercased; <c>?</c> when empty.</summary>
    public static char AvatarLetter(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return '?';

        var trimmed = title.Trim();
        return char.ToUpperInvariant(trimmed[0]);
    }

    /// <summary>Pinned first, then newest <see cref="DialogItem.LastMessageAt"/> descending.</summary>
    public static IReadOnlyList<DialogItem> SortDialogs(IEnumerable<DialogItem> dialogs)
    {
        ArgumentNullException.ThrowIfNull(dialogs);

        return dialogs
            .OrderByDescending(d => d.IsPinned)
            .ThenByDescending(d => d.LastMessageAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// Indexes the highest-id message per marked peer id (Bot API–style).
    /// Must not use raw <see cref="Peer.ID"/> — user/chat/channel ids collide.
    /// </summary>
    public static Dictionary<long, MessageBase> IndexTopMessagesByMarkedPeer(IEnumerable<MessageBase> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var topByPeer = new Dictionary<long, MessageBase>();
        foreach (var msg in messages)
        {
            if (msg.Peer is null)
                continue;

            var key = PeerId.FromPeer(msg.Peer).Value;
            if (!topByPeer.TryGetValue(key, out var existing) || msg.ID > existing.ID)
                topByPeer[key] = msg;
        }

        return topByPeer;
    }

    /// <summary>
    /// Resolves a dialog's top message using a marked-peer index, with linear fallback.
    /// </summary>
    public static MessageBase? ResolveTopMessage(
        Dialog dialog,
        IReadOnlyDictionary<long, MessageBase> topByMarkedPeer,
        IEnumerable<MessageBase> allMessages)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(topByMarkedPeer);
        ArgumentNullException.ThrowIfNull(allMessages);

        if (dialog.Peer is null)
            return null;

        var key = PeerId.FromPeer(dialog.Peer).Value;
        if (topByMarkedPeer.TryGetValue(key, out var candidate)
            && candidate.ID == dialog.TopMessage)
        {
            return candidate;
        }

        return allMessages.FirstOrDefault(m =>
            m.Peer is not null
            && PeerId.FromPeer(m.Peer).Value == key
            && m.ID == dialog.TopMessage);
    }

    public static string FormatUserTitle(UserBase? user, long? selfUserId = null)
    {
        if (user is null)
            return DeletedAccountTitle;

        if (selfUserId is long self && user.ID == self)
            return SavedMessagesTitle;

        if (user is not User u)
            return DeletedAccountTitle;

        var first = u.first_name?.Trim() ?? string.Empty;
        var last = u.last_name?.Trim() ?? string.Empty;
        var full = string.Join(' ', new[] { first, last }.Where(s => s.Length > 0));
        return full.Length > 0 ? full : DeletedAccountTitle;
    }

    public static string FormatChatTitle(ChatBase? chat) =>
        chat switch
        {
            null => DeletedAccountTitle,
            ChannelForbidden cf when !string.IsNullOrWhiteSpace(cf.title) => cf.title,
            _ when !string.IsNullOrWhiteSpace(chat.Title) => chat.Title,
            _ => DeletedAccountTitle
        };

    public static string PreviewFromMessage(MessageBase? messageBase)
    {
        if (messageBase is not Message msg)
            return string.Empty;

        var text = msg.message;
        var hasPhoto = msg.media is MessageMediaPhoto;
        var hasDocument = msg.media is MessageMediaDocument;
        string? documentName = null;

        if (msg.media is MessageMediaDocument { document: Document doc })
        {
            if (doc.GetAttribute<DocumentAttributeSticker>() is not null)
                documentName = "sticker";
            else
                documentName = doc.Filename;
        }

        return Preview(string.IsNullOrEmpty(text) ? null : text, hasPhoto, hasDocument, documentName);
    }

    public static MediaAttachment? MapMedia(Message msg, ChatId chatId)
    {
        ArgumentNullException.ThrowIfNull(msg);

        switch (msg.media)
        {
            case MessageMediaPhoto:
                return new MediaAttachment
                {
                    Kind = "photo",
                    SourceChatId = chatId,
                    SourceMessageId = new MessageId(msg.id)
                };

            case MessageMediaDocument { document: Document doc }:
            {
                var isSticker = doc.GetAttribute<DocumentAttributeSticker>() is not null;
                return new MediaAttachment
                {
                    Kind = isSticker ? "sticker" : "document",
                    FileName = doc.Filename,
                    MimeType = doc.mime_type,
                    SizeBytes = doc.size,
                    SourceChatId = chatId,
                    SourceMessageId = new MessageId(msg.id)
                };
            }

            default:
                return null;
        }
    }

    public static ChatMessage MapMessage(Message msg, ChatId chatId, int? readInboxMaxId = null, int? readOutboxMaxId = null)
    {
        ArgumentNullException.ThrowIfNull(msg);

        var isOutgoing = (msg.flags & Message.Flags.out_) != 0;
        var isEdited = (msg.flags & Message.Flags.has_edit_date) != 0;
        MessageId? replyTo = null;
        if (msg.reply_to is MessageReplyHeader header && header.reply_to_msg_id != 0)
            replyTo = new MessageId(header.reply_to_msg_id);

        var isRead = isOutgoing
            ? readOutboxMaxId is int outMax && msg.id <= outMax
            : readInboxMaxId is int inMax && msg.id <= inMax;

        // When read markers are unknown, treat as read so UI does not mark everything unread.
        if (readInboxMaxId is null && readOutboxMaxId is null)
            isRead = true;

        return new ChatMessage
        {
            Id = new MessageId(msg.id),
            ChatId = chatId,
            Text = msg.message ?? string.Empty,
            IsOutgoing = isOutgoing,
            SentAt = ToOffset(msg.date),
            IsEdited = isEdited,
            ReplyToId = replyTo,
            Media = MapMedia(msg, chatId),
            IsRead = isRead
        };
    }

    public static DialogItem MapDialog(
        Dialog dialog,
        IPeerInfo? peerInfo,
        MessageBase? topMessage,
        long? selfUserId = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var chatId = PeerId.FromPeer(dialog.peer);
        var isChannel = peerInfo is Channel || dialog.peer is PeerChannel;

        string title = peerInfo switch
        {
            UserBase user => FormatUserTitle(user, selfUserId),
            ChatBase chat => FormatChatTitle(chat),
            _ => DeletedAccountTitle
        };

        var isPinned = (dialog.flags & Dialog.Flags.pinned) != 0;
        var isMuted = IsMuted(dialog.notify_settings);
        var avatar = isChannel ? '#' : AvatarLetter(title);

        DateTimeOffset? lastAt = topMessage is not null
            ? ToOffset(topMessage.Date)
            : null;

        return new DialogItem
        {
            Id = chatId,
            Title = title,
            LastMessagePreview = PreviewFromMessage(topMessage),
            LastMessageAt = lastAt,
            UnreadCount = dialog.unread_count,
            IsPinned = isPinned,
            IsMuted = isMuted,
            AvatarLetter = avatar
        };
    }

    public static bool IsMuted(PeerNotifySettings? settings)
    {
        if (settings is null)
            return false;

        if ((settings.flags & PeerNotifySettings.Flags.has_mute_until) == 0)
            return false;

        // mute_until in the future means still muted.
        return settings.mute_until > DateTime.UtcNow;
    }

    public static DateTimeOffset ToOffset(DateTime date)
    {
        if (date.Kind == DateTimeKind.Unspecified)
            return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc));
        return new DateTimeOffset(date.ToUniversalTime());
    }
}
