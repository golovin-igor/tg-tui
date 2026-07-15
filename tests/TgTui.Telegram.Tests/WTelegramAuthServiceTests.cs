using FluentAssertions;
using TgTui.Core.Config;
using TgTui.Core.Events;
using TgTui.Core.Models;
using TgTui.Core.Paths;
using TgTui.Telegram;
using WTelegram;

public class WTelegramAuthServiceTests
{
    [Fact]
    public async Task Start_without_credentials_is_NeedsCredentials()
    {
        await using var fixture = await AuthFixture.CreateAsync(config: new AppConfig());
        var events = new List<AuthStateChanged>();
        fixture.Hub.AuthStateChanged += e => events.Add(e);

        await fixture.Service.StartAsync();

        fixture.Service.Current.Phase.Should().Be(AuthPhase.NeedsCredentials);
        events.Should().ContainSingle(e => e.State.Phase == AuthPhase.NeedsCredentials);
    }

    [Fact]
    public async Task SubmitCredentials_persists_and_moves_to_NeedsPhone()
    {
        await using var fixture = await AuthFixture.CreateAsync(config: new AppConfig());

        await fixture.Service.SubmitCredentialsAsync(12345, "0123456789abcdef0123456789abcdef");

        fixture.Service.Current.Phase.Should().Be(AuthPhase.NeedsPhone);

        var reloaded = new ConfigStore(fixture.Paths.ConfigFile);
        await reloaded.LoadAsync();
        reloaded.Current.ApiId.Should().Be(12345);
        reloaded.Current.ApiHash.Should().Be("0123456789abcdef0123456789abcdef");
    }

    private const string ValidApiHash = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Full_login_flow_with_stubbed_login_reaches_Ready()
    {
        await using var fixture = await AuthFixture.CreateAsync(
            config: new AppConfig { ApiId = 1, ApiHash = ValidApiHash },
            loginResponses: new Queue<string?>(new string?[]
            {
                "phone_number",       // StartAsync → NeedsPhone
                "verification_code",  // SubmitPhone → NeedsCode
                "password",           // SubmitCode → NeedsPassword
                null                  // SubmitPassword → Ready
            }));

        await fixture.Service.StartAsync();
        fixture.Service.Current.Phase.Should().Be(AuthPhase.NeedsPhone);

        await fixture.Service.SubmitPhoneAsync("+15551234567");
        fixture.Service.Current.Phase.Should().Be(AuthPhase.NeedsCode);

        await fixture.Service.SubmitCodeAsync("12345");
        fixture.Service.Current.Phase.Should().Be(AuthPhase.NeedsPassword);

        await fixture.Service.SubmitPasswordAsync("secret");
        fixture.Service.Current.Phase.Should().Be(AuthPhase.Ready);
    }

    [Fact]
    public async Task Start_with_existing_session_stub_is_Ready()
    {
        await using var fixture = await AuthFixture.CreateAsync(
            config: new AppConfig { ApiId = 1, ApiHash = ValidApiHash },
            loginResponses: new Queue<string?>(new string?[] { null }));

        await fixture.Service.StartAsync();
        fixture.Service.Current.Phase.Should().Be(AuthPhase.Ready);
    }

    [Fact]
    public async Task Login_error_sets_Failed_with_message()
    {
        await using var fixture = await AuthFixture.CreateAsync(
            config: new AppConfig { ApiId = 1, ApiHash = ValidApiHash },
            loginError: new InvalidOperationException("FLOOD_WAIT_30"));

        await fixture.Service.StartAsync();
        fixture.Service.Current.Phase.Should().Be(AuthPhase.Failed);
        fixture.Service.Current.Message.Should().Contain("FLOOD_WAIT");
    }

    [Fact]
    public async Task TelegramClientFactory_creates_client_with_session_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "tg-tui-factory-" + Guid.NewGuid().ToString("N"));
        var paths = AppPaths.ForRoot(root);
        paths.EnsureCreated();
        try
        {
            using var client = TelegramClientFactory.Create(paths, what => what switch
            {
                "api_id" => "1",
                "api_hash" => "0123456789abcdef0123456789abcdef",
                _ => null
            });
            client.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class AuthFixture : IAsyncDisposable
    {
        private AuthFixture(
            AppPaths paths,
            ConfigStore store,
            TelegramSession session,
            WTelegramUpdateHub hub,
            WTelegramAuthService service)
        {
            Paths = paths;
            Store = store;
            Session = session;
            Hub = hub;
            Service = service;
        }

        public AppPaths Paths { get; }
        public ConfigStore Store { get; }
        public TelegramSession Session { get; }
        public WTelegramUpdateHub Hub { get; }
        public WTelegramAuthService Service { get; }

        public static async Task<AuthFixture> CreateAsync(
            AppConfig config,
            Queue<string?>? loginResponses = null,
            Exception? loginError = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "tg-tui-auth-" + Guid.NewGuid().ToString("N"));
            var paths = AppPaths.ForRoot(root);
            paths.EnsureCreated();

            var store = new ConfigStore(paths.ConfigFile);
            store.Current.ApiId = config.ApiId;
            store.Current.ApiHash = config.ApiHash;
            if (config.HasCredentials)
                await store.SaveAsync();

            var session = new TelegramSession();
            var hub = new WTelegramUpdateHub();

            Client FakeFactory(AppPaths p, Func<string, string?> cfg)
            {
                // Real Client ctor may touch session file but does not need network until Login.
                return TelegramClientFactory.Create(p, cfg);
            }

            Task<string?> FakeLogin(Client _, string? __, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (loginError is not null)
                    throw loginError;
                if (loginResponses is null || loginResponses.Count == 0)
                    return Task.FromResult<string?>("phone_number");
                return Task.FromResult(loginResponses.Dequeue());
            }

            var service = new WTelegramAuthService(paths, store, session, hub, FakeFactory, FakeLogin);
            return new AuthFixture(paths, store, session, hub, service);
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            if (Directory.Exists(Paths.Root))
                Directory.Delete(Paths.Root, true);
        }
    }
}
