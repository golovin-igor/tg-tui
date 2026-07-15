namespace TgTui.Core.Models;

public sealed class AuthState(AuthPhase Phase, string? Message = null)
{
    public AuthPhase Phase { get; } = Phase;
    public string? Message { get; } = Message;
}
