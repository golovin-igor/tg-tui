using TgTui.Core.Models;

namespace TgTui.Core.Ports;

public interface IAuthService
{
    AuthState Current { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task SubmitCredentialsAsync(int apiId, string apiHash, CancellationToken cancellationToken = default);
    Task SubmitPhoneAsync(string phone, CancellationToken cancellationToken = default);
    Task SubmitCodeAsync(string code, CancellationToken cancellationToken = default);
    Task SubmitPasswordAsync(string password, CancellationToken cancellationToken = default);
}
