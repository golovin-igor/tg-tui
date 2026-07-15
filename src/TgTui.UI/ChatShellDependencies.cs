using TgTui.Core.Ports;

namespace TgTui.UI;

/// <summary>
/// Services required by <see cref="Views.ChatShellView"/> after authentication (or fake mode).
/// </summary>
public sealed record ChatShellDependencies(
    IDialogService Dialogs,
    IMessageService Messages,
    IDraftStore Drafts,
    IMediaService Media,
    IUpdateHub Updates);
