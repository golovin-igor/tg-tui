namespace TgTui.Core.Auth;

public enum AuthEvent
{
    MissingCredentials,
    CredentialsSubmitted,
    PhoneSubmitted,
    CodeSubmitted,
    PasswordRequired,
    PasswordSubmitted,
    LoggedIn,
    Error
}
