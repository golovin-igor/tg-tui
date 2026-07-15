namespace TgTui.Core.Models;

public enum AuthPhase
{
    NeedsCredentials,
    NeedsPhone,
    NeedsCode,
    NeedsPassword,
    Ready,
    Failed
}
