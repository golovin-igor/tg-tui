using TgTui.Core.Models;

namespace TgTui.Core.Auth;

/// <summary>
/// Pure auth phase transitions — unit-testable without Telegram network.
/// </summary>
public static class AuthFlow
{
    public static AuthPhase NextPhase(AuthPhase current, AuthEvent ev) =>
        (current, ev) switch
        {
            (_, AuthEvent.LoggedIn) => AuthPhase.Ready,
            (_, AuthEvent.MissingCredentials) => AuthPhase.NeedsCredentials,
            (_, AuthEvent.Error) => AuthPhase.Failed,

            (AuthPhase.NeedsCredentials, AuthEvent.CredentialsSubmitted) => AuthPhase.NeedsPhone,
            (AuthPhase.Failed, AuthEvent.CredentialsSubmitted) => AuthPhase.NeedsPhone,

            (AuthPhase.NeedsPhone, AuthEvent.PhoneSubmitted) => AuthPhase.NeedsCode,
            (AuthPhase.Failed, AuthEvent.PhoneSubmitted) => AuthPhase.NeedsCode,

            (AuthPhase.NeedsCode, AuthEvent.CodeSubmitted) => AuthPhase.Ready,
            (AuthPhase.NeedsCode, AuthEvent.PasswordRequired) => AuthPhase.NeedsPassword,
            (AuthPhase.Failed, AuthEvent.CodeSubmitted) => AuthPhase.Ready,
            (AuthPhase.Failed, AuthEvent.PasswordRequired) => AuthPhase.NeedsPassword,

            (AuthPhase.NeedsPassword, AuthEvent.PasswordSubmitted) => AuthPhase.Ready,
            (AuthPhase.Failed, AuthEvent.PasswordSubmitted) => AuthPhase.Ready,

            _ => current
        };
}
