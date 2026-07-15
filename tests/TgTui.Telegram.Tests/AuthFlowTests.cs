using FluentAssertions;
using TgTui.Core.Auth;
using TgTui.Core.Models;

public class AuthFlowTests
{
    [Theory]
    [InlineData(AuthPhase.NeedsCredentials, AuthEvent.CredentialsSubmitted, AuthPhase.NeedsPhone)]
    [InlineData(AuthPhase.NeedsPhone, AuthEvent.PhoneSubmitted, AuthPhase.NeedsCode)]
    [InlineData(AuthPhase.NeedsCode, AuthEvent.CodeSubmitted, AuthPhase.Ready)]
    [InlineData(AuthPhase.NeedsCode, AuthEvent.PasswordRequired, AuthPhase.NeedsPassword)]
    [InlineData(AuthPhase.NeedsPassword, AuthEvent.PasswordSubmitted, AuthPhase.Ready)]
    [InlineData(AuthPhase.NeedsPhone, AuthEvent.LoggedIn, AuthPhase.Ready)]
    [InlineData(AuthPhase.NeedsCode, AuthEvent.Error, AuthPhase.Failed)]
    [InlineData(AuthPhase.NeedsCredentials, AuthEvent.MissingCredentials, AuthPhase.NeedsCredentials)]
    public void NextPhase_happy_and_error_paths(AuthPhase current, AuthEvent ev, AuthPhase expected)
    {
        AuthFlow.NextPhase(current, ev).Should().Be(expected);
    }

    [Theory]
    [InlineData(AuthEvent.CredentialsSubmitted, AuthPhase.NeedsPhone)]
    [InlineData(AuthEvent.PhoneSubmitted, AuthPhase.NeedsCode)]
    [InlineData(AuthEvent.CodeSubmitted, AuthPhase.Ready)]
    [InlineData(AuthEvent.PasswordRequired, AuthPhase.NeedsPassword)]
    [InlineData(AuthEvent.PasswordSubmitted, AuthPhase.Ready)]
    public void Failed_allows_retry_via_same_events(AuthEvent ev, AuthPhase expected)
    {
        AuthFlow.NextPhase(AuthPhase.Failed, ev).Should().Be(expected);
    }

    [Fact]
    public void Unknown_transition_keeps_current_phase()
    {
        AuthFlow.NextPhase(AuthPhase.Ready, AuthEvent.PhoneSubmitted).Should().Be(AuthPhase.Ready);
        AuthFlow.NextPhase(AuthPhase.NeedsPhone, AuthEvent.PasswordSubmitted).Should().Be(AuthPhase.NeedsPhone);
    }
}
