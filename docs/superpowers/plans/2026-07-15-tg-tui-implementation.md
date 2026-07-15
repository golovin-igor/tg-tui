# tg-tui Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a daily-driver Telegram TUI client on .NET 10 with Terminal.Gui + WTelegramClient, smart inline media, Telegram Desktop–inspired keyboard UX, self-contained multi-platform publish, and a complete GitHub docs package.

**Architecture:** Layered solution (`UI → Core ← Telegram/Media`). Host wires DI. WTelegramClient stays behind Core ports. UI marshals all updates onto the Terminal.Gui main loop. Media downloads on demand and renders via Kitty/iTerm/Sixel when available, else half-block, always with external open.

**Tech Stack:** .NET 10, Terminal.Gui (v2), WTelegramClient, Spectre.Console, Tomlyn (TOML config), xUnit + FluentAssertions, Microsoft.Extensions.DependencyInjection / Hosting / Logging.

**Spec:** `docs/superpowers/specs/2026-07-15-tg-tui-design.md`

## Global Constraints

- Target framework: `net10.0`
- One type per file; file name matches type name
- SOLID / DRY / SRP; search before inventing helpers
- No live Telegram network in CI; secrets never committed
- Self-contained publish RIDs: `linux-x64`, `osx-x64`, `osx-arm64`, `win-x64`
- Default theme: Telegram Desktop–inspired (`#0e1621`, `#17212b`, `#2b5278`, `#5eb5f7`)
- User runs all `git commit` / `git push` themselves — plan marks **Suggested commit** steps for the human; agents must not commit unless the user explicitly overrides
- No Co-Authored-By lines in commit messages

---

## File structure (create during tasks)

```
tg-tui/
├── TgTui.slnx                          # or TgTui.sln if slnx unsupported in tooling
├── global.json
├── .editorconfig
├── .gitignore
├── Directory.Build.props
├── src/
│   ├── TgTui/
│   │   ├── TgTui.csproj
│   │   ├── Program.cs
│   │   └── appsettings.json            # non-secret defaults only
│   ├── TgTui.Core/
│   │   ├── TgTui.Core.csproj
│   │   ├── Models/
│   │   │   ├── ChatId.cs
│   │   │   ├── DialogItem.cs
│   │   │   ├── ChatMessage.cs
│   │   │   ├── MessageId.cs
│   │   │   ├── AuthState.cs
│   │   │   └── MediaAttachment.cs
│   │   ├── Ports/
│   │   │   ├── IAuthService.cs
│   │   │   ├── IDialogService.cs
│   │   │   ├── IMessageService.cs
│   │   │   ├── IDraftStore.cs
│   │   │   ├── IMediaService.cs
│   │   │   └── IUpdateHub.cs
│   │   ├── Events/
│   │   │   ├── DialogsChanged.cs
│   │   │   ├── MessagesChanged.cs
│   │   │   ├── ConnectionStateChanged.cs
│   │   │   └── AuthStateChanged.cs
│   │   ├── Drafts/
│   │   │   └── FileDraftStore.cs
│   │   ├── Filtering/
│   │   │   └── DialogFilter.cs
│   │   └── Paths/
│   │       └── AppPaths.cs
│   ├── TgTui.Telegram/
│   │   ├── TgTui.Telegram.csproj
│   │   ├── WTelegramAuthService.cs
│   │   ├── WTelegramDialogService.cs
│   │   ├── WTelegramMessageService.cs
│   │   ├── WTelegramUpdateHub.cs
│   │   ├── TelegramClientFactory.cs
│   │   └── Mapping/
│   │       └── TelegramMapper.cs
│   ├── TgTui.Media/
│   │   ├── TgTui.Media.csproj
│   │   ├── MediaService.cs
│   │   ├── TerminalCapabilityDetector.cs
│   │   ├── GraphicsCapability.cs
│   │   ├── HalfBlockImageRenderer.cs
│   │   ├── ProtocolImageRenderer.cs
│   │   └── MediaCache.cs
│   └── TgTui.UI/
│       ├── TgTui.UI.csproj
│       ├── AppRunner.cs
│       ├── Theme/
│       │   └── TelegramDesktopTheme.cs
│       ├── Keymap/
│       │   └── KeyBindings.cs
│       ├── Views/
│       │   ├── AuthWizardView.cs
│       │   ├── ChatShellView.cs
│       │   ├── DialogListView.cs
│       │   ├── MessagePaneView.cs
│       │   ├── ComposerView.cs
│       │   ├── HelpOverlay.cs
│       │   └── StatusBarView.cs
│       └── ViewModels/
│           ├── DialogListViewModel.cs
│           ├── MessagePaneViewModel.cs
│           └── ComposerViewModel.cs
├── tests/
│   ├── TgTui.Core.Tests/
│   ├── TgTui.Media.Tests/
│   └── TgTui.Telegram.Tests/
├── .github/
│   ├── workflows/ci.yml
│   ├── workflows/release.yml
│   ├── dependabot.yml
│   ├── PULL_REQUEST_TEMPLATE.md
│   └── ISSUE_TEMPLATE/
│       ├── bug_report.md
│       └── feature_request.md
├── README.md
├── CONTRIBUTING.md
├── CODE_OF_CONDUCT.md
├── SECURITY.md
├── LICENSE
└── CHANGELOG.md
```

---

### Task 1: Solution scaffold and repo hygiene

**Files:**
- Create: `global.json`, `.editorconfig`, `.gitignore`, `Directory.Build.props`, `TgTui.sln`
- Create: `src/TgTui/TgTui.csproj`, `src/TgTui/Program.cs`
- Create: `src/TgTui.Core/TgTui.Core.csproj`
- Create: `src/TgTui.Telegram/TgTui.Telegram.csproj`
- Create: `src/TgTui.Media/TgTui.Media.csproj`
- Create: `src/TgTui.UI/TgTui.UI.csproj`
- Create: `tests/TgTui.Core.Tests/TgTui.Core.Tests.csproj`
- Create: `tests/TgTui.Media.Tests/TgTui.Media.Tests.csproj`
- Create: `tests/TgTui.Telegram.Tests/TgTui.Telegram.Tests.csproj`

**Interfaces:**
- Consumes: none
- Produces: buildable empty solution; project references Host→UI,Telegram,Media,Core; UI→Core,Media; Telegram→Core; Media→Core

- [ ] **Step 1: Pin SDK and ignore junk**

`global.json`:
```json
{
  "sdk": {
    "version": "10.0.300",
    "rollForward": "latestFeature"
  }
}
```

`.gitignore` must include at minimum:
```
bin/
obj/
.vs/
.idea/
*.user
.DS_Store
.superpowers/
publish/
artifacts/
**/session.dat
**/config.toml
**/cache/
**/logs/
**/drafts.json
```

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create solution and projects**

```bash
cd /Users/igor/Projects/tg-tui
dotnet new sln -n TgTui
dotnet new console -n TgTui -o src/TgTui --framework net10.0
dotnet new classlib -n TgTui.Core -o src/TgTui.Core --framework net10.0
dotnet new classlib -n TgTui.Telegram -o src/TgTui.Telegram --framework net10.0
dotnet new classlib -n TgTui.Media -o src/TgTui.Media --framework net10.0
dotnet new classlib -n TgTui.UI -o src/TgTui.UI --framework net10.0
dotnet new xunit -n TgTui.Core.Tests -o tests/TgTui.Core.Tests --framework net10.0
dotnet new xunit -n TgTui.Media.Tests -o tests/TgTui.Media.Tests --framework net10.0
dotnet new xunit -n TgTui.Telegram.Tests -o tests/TgTui.Telegram.Tests --framework net10.0
dotnet sln add src/TgTui src/TgTui.Core src/TgTui.Telegram src/TgTui.Media src/TgTui.UI \
  tests/TgTui.Core.Tests tests/TgTui.Media.Tests tests/TgTui.Telegram.Tests
```

Remove default `Class1.cs` / sample tests.

- [ ] **Step 3: Wire project references and packages**

Host `TgTui.csproj` references: UI, Telegram, Media, Core.  
UI → Core, Media. Telegram → Core. Media → Core.  
Test projects reference their SUT + FluentAssertions.

```bash
dotnet add src/TgTui package Microsoft.Extensions.Hosting
dotnet add src/TgTui package Microsoft.Extensions.Logging.Console
dotnet add src/TgTui.UI package Terminal.Gui
dotnet add src/TgTui.UI package Spectre.Console
dotnet add src/TgTui.Telegram package WTelegramClient
dotnet add src/TgTui.Media package Spectre.Console
dotnet add src/TgTui.Core package Tomlyn
dotnet add tests/TgTui.Core.Tests package FluentAssertions
dotnet add tests/TgTui.Media.Tests package FluentAssertions
dotnet add tests/TgTui.Telegram.Tests package FluentAssertions
dotnet add src/TgTui reference src/TgTui.UI src/TgTui.Telegram src/TgTui.Media src/TgTui.Core
dotnet add src/TgTui.UI reference src/TgTui.Core src/TgTui.Media
dotnet add src/TgTui.Telegram reference src/TgTui.Core
dotnet add src/TgTui.Media reference src/TgTui.Core
dotnet add tests/TgTui.Core.Tests reference src/TgTui.Core
dotnet add tests/TgTui.Media.Tests reference src/TgTui.Media
dotnet add tests/TgTui.Telegram.Tests reference src/TgTui.Telegram
```

- [ ] **Step 4: Minimal Program.cs and build**

```csharp
// src/TgTui/Program.cs
Console.WriteLine("tg-tui scaffold OK");
```

```bash
dotnet build
dotnet test --no-build
```

Expected: Build succeeded, 0 tests or empty pass.

- [ ] **Step 5: Suggested commit (user runs)**

```bash
git add global.json .editorconfig .gitignore Directory.Build.props TgTui.sln src tests
git commit -m "chore: scaffold .NET 10 solution for tg-tui"
```

---

### Task 2: Core models, paths, and ports

**Files:**
- Create: all `src/TgTui.Core/Models/*`, `Ports/*`, `Events/*`, `Paths/AppPaths.cs`
- Test: `tests/TgTui.Core.Tests/AppPathsTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct ChatId(long Value);`
  - `readonly record struct MessageId(long Value);`
  - `sealed class DialogItem` with `ChatId Id`, `string Title`, `string LastMessagePreview`, `DateTimeOffset? LastMessageAt`, `int UnreadCount`, `bool IsPinned`, `bool IsMuted`, `char AvatarLetter`
  - `sealed class ChatMessage` with `MessageId Id`, `ChatId ChatId`, `string Text`, `bool IsOutgoing`, `DateTimeOffset SentAt`, `bool IsEdited`, `MessageId? ReplyToId`, `MediaAttachment? Media`, `bool IsRead`
  - `sealed class MediaAttachment` with `string Kind`, `string? FileName`, `string? LocalPath`, `string? MimeType`, `long? SizeBytes`
  - `enum AuthPhase { NeedsCredentials, NeedsPhone, NeedsCode, NeedsPassword, Ready, Failed }`
  - `sealed class AuthState(AuthPhase Phase, string? Message = null)`
  - Ports as async interfaces below
  - `static class AppPaths` with `Root`, `ConfigFile`, `SessionFile`, `DraftsFile`, `MediaCacheDir`, `LogsDir` and `EnsureCreated()`

Port signatures (exact):

```csharp
public interface IAuthService
{
    AuthState Current { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task SubmitCredentialsAsync(int apiId, string apiHash, CancellationToken cancellationToken = default);
    Task SubmitPhoneAsync(string phone, CancellationToken cancellationToken = default);
    Task SubmitCodeAsync(string code, CancellationToken cancellationToken = default);
    Task SubmitPasswordAsync(string password, CancellationToken cancellationToken = default);
}

public interface IDialogService
{
    Task<IReadOnlyList<DialogItem>> GetDialogsAsync(CancellationToken cancellationToken = default);
    Task SetMutedAsync(ChatId chatId, bool muted, CancellationToken cancellationToken = default);
    Task SetPinnedAsync(ChatId chatId, bool pinned, CancellationToken cancellationToken = default);
}

public interface IMessageService
{
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(ChatId chatId, MessageId? beforeId, int limit, CancellationToken cancellationToken = default);
    Task<ChatMessage> SendTextAsync(ChatId chatId, string text, MessageId? replyToId, CancellationToken cancellationToken = default);
    Task EditTextAsync(ChatId chatId, MessageId messageId, string text, CancellationToken cancellationToken = default);
    Task DeleteAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken = default);
}

public interface IDraftStore
{
    string? GetDraft(ChatId chatId);
    void SetDraft(ChatId chatId, string? text);
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public interface IMediaService
{
    Task<string?> EnsureLocalAsync(MediaAttachment media, CancellationToken cancellationToken = default);
    Task OpenExternallyAsync(string localPath, CancellationToken cancellationToken = default);
    string RenderPreview(string localPath, int maxCellWidth);
}

public interface IUpdateHub
{
    event Action<DialogsChanged>? DialogsChanged;
    event Action<MessagesChanged>? MessagesChanged;
    event Action<ConnectionStateChanged>? ConnectionStateChanged;
    event Action<AuthStateChanged>? AuthStateChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
}
```

Event records:

```csharp
public sealed record DialogsChanged;
public sealed record MessagesChanged(ChatId ChatId);
public sealed record ConnectionStateChanged(bool IsConnected, string? Detail);
public sealed record AuthStateChanged(AuthState State);
```

- [ ] **Step 1: Write failing AppPaths test**

```csharp
// tests/TgTui.Core.Tests/AppPathsTests.cs
using FluentAssertions;
using TgTui.Core.Paths;

public class AppPathsTests
{
    [Fact]
    public void EnsureCreated_creates_root_and_subdirs()
    {
        var root = Path.Combine(Path.GetTempPath(), "tg-tui-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.ForRoot(root);
            paths.EnsureCreated();
            Directory.Exists(paths.Root).Should().BeTrue();
            Directory.Exists(paths.MediaCacheDir).Should().BeTrue();
            Directory.Exists(paths.LogsDir).Should().BeTrue();
            paths.ConfigFile.Should().EndWith("config.toml");
            paths.SessionFile.Should().EndWith("session.dat");
            paths.DraftsFile.Should().EndWith("drafts.json");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
```

- [ ] **Step 2: Run test — expect fail**

```bash
dotnet test tests/TgTui.Core.Tests --filter AppPathsTests
```

Expected: compile error / missing type `AppPaths`.

- [ ] **Step 3: Implement models, ports, events, AppPaths**

`AppPaths` must expose `ForRoot(string root)` for tests and `ForCurrentUser()` using:

```csharp
// Linux/macOS: ~/.config/tg-tui
// Windows: %AppData%/tg-tui
var root = OperatingSystem.IsWindows()
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tg-tui")
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "tg-tui");
```

One type per file for every model, port, and event.

- [ ] **Step 4: Run tests — expect pass**

```bash
dotnet test tests/TgTui.Core.Tests --filter AppPathsTests
```

- [ ] **Step 5: Suggested commit**

```bash
git add src/TgTui.Core tests/TgTui.Core.Tests
git commit -m "feat(core): add domain models, ports, and app paths"
```

---

### Task 3: Dialog filter + file draft store (TDD)

**Files:**
- Create: `src/TgTui.Core/Filtering/DialogFilter.cs`
- Create: `src/TgTui.Core/Drafts/FileDraftStore.cs`
- Test: `tests/TgTui.Core.Tests/DialogFilterTests.cs`
- Test: `tests/TgTui.Core.Tests/FileDraftStoreTests.cs`

**Interfaces:**
- Consumes: `DialogItem`, `ChatId`, `IDraftStore`, `AppPaths`
- Produces:
  - `static class DialogFilter` with `IReadOnlyList<DialogItem> Apply(IReadOnlyList<DialogItem> source, string? query)`
  - `sealed class FileDraftStore : IDraftStore`

- [ ] **Step 1: Failing filter tests**

```csharp
public class DialogFilterTests
{
    private static DialogItem D(string title, bool pinned = false) => new()
    {
        Id = new ChatId(title.GetHashCode()),
        Title = title,
        LastMessagePreview = "",
        AvatarLetter = title[0],
        IsPinned = pinned
    };

    [Fact]
    public void Empty_query_returns_all_preserving_order()
    {
        var items = new[] { D("Alice"), D("Bob") };
        DialogFilter.Apply(items, null).Should().Equal(items);
        DialogFilter.Apply(items, "  ").Should().Equal(items);
    }

    [Fact]
    public void Filters_case_insensitive_by_title()
    {
        var items = new[] { D("Alice"), D("Bob"), D("Alicia") };
        DialogFilter.Apply(items, "ali").Select(x => x.Title)
            .Should().Equal("Alice", "Alicia");
    }
}
```

- [ ] **Step 2: Implement DialogFilter**

```csharp
public static class DialogFilter
{
    public static IReadOnlyList<DialogItem> Apply(IReadOnlyList<DialogItem> source, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return source;
        var q = query.Trim();
        return source.Where(d => d.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
```

- [ ] **Step 3: Failing draft store tests**

```csharp
public class FileDraftStoreTests
{
    [Fact]
    public async Task Set_get_and_persist_roundtrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "tg-tui-drafts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "drafts.json");
        try
        {
            var store = new FileDraftStore(path);
            await store.LoadAsync();
            store.SetDraft(new ChatId(1), "hello");
            store.GetDraft(new ChatId(1)).Should().Be("hello");
            await store.SaveAsync();

            var store2 = new FileDraftStore(path);
            await store2.LoadAsync();
            store2.GetDraft(new ChatId(1)).Should().Be("hello");

            store2.SetDraft(new ChatId(1), null);
            store2.GetDraft(new ChatId(1)).Should().BeNull();
            await store2.SaveAsync();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
```

- [ ] **Step 4: Implement FileDraftStore**

JSON dictionary `Dictionary<string, string>` keyed by chat id invariant string. `SetDraft` with null/whitespace removes key. Atomic write: write temp file then `File.Move` overwrite.

- [ ] **Step 5: Run all Core tests**

```bash
dotnet test tests/TgTui.Core.Tests
```

Expected: all pass.

- [ ] **Step 6: Suggested commit**

```bash
git commit -am "feat(core): dialog filter and file draft store"
```

---

### Task 4: Media capability detection + half-block renderer (TDD)

**Files:**
- Create: `src/TgTui.Media/GraphicsCapability.cs`
- Create: `src/TgTui.Media/TerminalCapabilityDetector.cs`
- Create: `src/TgTui.Media/HalfBlockImageRenderer.cs`
- Create: `src/TgTui.Media/MediaCache.cs`
- Create: `src/TgTui.Media/MediaService.cs` (stub OpenExternally + EnsureLocal for non-Telegram bytes later; EnsureLocal may accept already-local paths)
- Test: `tests/TgTui.Media.Tests/TerminalCapabilityDetectorTests.cs`
- Test: `tests/TgTui.Media.Tests/HalfBlockImageRendererTests.cs`
- Test: `tests/TgTui.Media.Tests/MediaCacheTests.cs`

**Interfaces:**
- Produces:
  - `enum GraphicsCapability { None, HalfBlock, Sixel, Kitty, ITerm2 }`
  - `sealed class TerminalCapabilityDetector` with `GraphicsCapability Detect(IReadOnlyDictionary<string, string?> environment, string? termProgram = null)`
  - `static class HalfBlockImageRenderer` with `string RenderFile(string path, int maxCellWidth)` returning multi-line Spectre-compatible or plain ANSI half-block text; on unreadable file returns placeholder string `"🖼 image unavailable"`
  - `sealed class MediaCache` with `string GetPath(string key)`, `bool Exists(string key)`, `void Put(string key, Stream content)`
  - `MediaService` implements `IMediaService` for local path preview/open; Telegram download filled in Task 7

Detection rules (exact):
1. If `KITTY_WINDOW_ID` set or `TERM` contains `kitty` → `Kitty`
2. Else if `TERM_PROGRAM` is `iTerm.app` → `ITerm2`
3. Else if `TERM` contains `xterm` and env `TERM`/`COLORTERM` suggests modern (or `WT_SESSION` for Windows Terminal) → prefer `Sixel` when `TERM` is `xterm-256color` / `foot` / `wezterm` / `mlterm` / `contour` OR `TERM_PROGRAM` is `WezTerm`
4. Else → `HalfBlock` if stdout is interactive; tests pass env explicitly
5. `None` only if forced via env `TG_TUI_GRAPHICS=none`

Also honor `TG_TUI_GRAPHICS=kitty|sixel|iterm|half|none` override.

- [ ] **Step 1: Failing capability tests**

```csharp
[Theory]
[InlineData("kitty", "xterm-kitty", null, GraphicsCapability.Kitty)]
[InlineData(null, "xterm-256color", "WezTerm", GraphicsCapability.Sixel)]
[InlineData(null, "xterm-256color", "iTerm.app", GraphicsCapability.ITerm2)]
[InlineData(null, "dumb", null, GraphicsCapability.HalfBlock)]
public void Detects(string? kittyWindow, string? term, string? termProgram, GraphicsCapability expected)
{
    var env = new Dictionary<string, string?> { ["TERM"] = term };
    if (kittyWindow is not null) env["KITTY_WINDOW_ID"] = kittyWindow;
    new TerminalCapabilityDetector().Detect(env, termProgram).Should().Be(expected);
}

[Fact]
public void Override_none()
{
    var env = new Dictionary<string, string?> { ["TG_TUI_GRAPHICS"] = "none", ["TERM"] = "xterm-kitty" };
    new TerminalCapabilityDetector().Detect(env, null).Should().Be(GraphicsCapability.None);
}
```

- [ ] **Step 2: Implement detector to pass**

- [ ] **Step 3: Half-block test with tiny generated PNG**

Create a 2×2 PNG in test temp via `SkiaSharp` **or** embed a minimal base64 1×1 PNG and assert:
- `RenderFile` does not throw
- result is non-empty
- missing file returns `"🖼 image unavailable"`

If adding SkiaSharp only for tests is heavy, implement renderer with `SixLabors.ImageSharp` in Media project (add package) — preferred for decode.

```bash
dotnet add src/TgTui.Media package SixLabors.ImageSharp
```

Renderer algorithm (v1): load image, resize max width `maxCellWidth`, height in half-block rows `ceil(h/2)`, emit Unicode `▄` with ANSI truecolor fg/bg pairs (Spectre markup optional). Keep pure string return for UI to host in a view.

- [ ] **Step 4: MediaCache tests** — put/get exists roundtrip under temp root

- [ ] **Step 5: MediaService OpenExternally**

```csharp
// macOS: open, Linux: xdg-open, Windows: use ProcessStartInfo UseShellExecute=true
```

Unit-test OpenExternally only via a test seam `IProcessLauncher` if needed; otherwise skip process launch in unit tests and test path selection method `GetOpenCommand(string path)`.

- [ ] **Step 6: `dotnet test tests/TgTui.Media.Tests` — all pass**

- [ ] **Step 7: Suggested commit**

```bash
git commit -am "feat(media): capability detection, cache, half-block preview"
```

---

### Task 5: Telegram gateway — config + auth service

**Files:**
- Create: `src/TgTui.Core/Config/AppConfig.cs` (if not exists): `int? ApiId`, `string? ApiHash`
- Create: `src/TgTui.Core/Config/ConfigStore.cs` — load/save TOML via Tomlyn
- Create: `src/TgTui.Telegram/TelegramClientFactory.cs`
- Create: `src/TgTui.Telegram/WTelegramAuthService.cs`
- Create: `src/TgTui.Telegram/WTelegramUpdateHub.cs` (can start empty events)
- Test: `tests/TgTui.Core.Tests/ConfigStoreTests.cs`
- Test: `tests/TgTui.Telegram.Tests/WTelegramAuthServiceTests.cs` (fake config provider; no network — test state machine transitions with injected `Func` stubs if needed)

**Interfaces:**
- Produces working `IAuthService` against real WTelegramClient when credentials valid
- `TelegramClientFactory.Create(AppPaths paths, Func<string, string?> config)` matching WTelegramClient config callback: `api_id`, `api_hash`, `phone_number`, `verification_code`, `password`, `session_pathname`

Auth state machine:
- No api → `NeedsCredentials`
- Has api, no session user → after Start, if not logged in → `NeedsPhone`
- After phone → `NeedsCode`
- After code, if password required → `NeedsPassword`
- Logged in → `Ready`
- Errors → `Failed` with message, allow retry by re-entering previous phase

Implementation notes:
- Use `client.LoginUserIfNeeded()` pattern from WTelegramClient docs; feed values from fields set by Submit* methods.
- Session path = `paths.SessionFile`.
- Raise `IUpdateHub` / auth events on phase change.

- [ ] **Step 1: ConfigStore TDD** — write TOML with api_id/api_hash, reload equals

Example TOML:
```toml
api_id = 12345
api_hash = "0123456789abcdef0123456789abcdef"
```

- [ ] **Step 2: Implement ConfigStore + AppConfig**

- [ ] **Step 3: Implement TelegramClientFactory + WTelegramAuthService**

Keep client instance owned by a `TelegramSession` singleton registered in DI later.

- [ ] **Step 4: Unit test auth phase without network**

Extract `AuthFlow` pure helper if needed:

```csharp
public static AuthPhase NextPhase(AuthPhase current, AuthEvent ev);
```

Test transitions only; integration with real Telegram is manual.

- [ ] **Step 5: `dotnet test` Core + Telegram tests**

- [ ] **Step 6: Suggested commit**

```bash
git commit -am "feat(telegram): config store and auth state machine"
```

---

### Task 6: Telegram dialog + message services + mapper

**Files:**
- Create: `src/TgTui.Telegram/Mapping/TelegramMapper.cs`
- Create: `src/TgTui.Telegram/WTelegramDialogService.cs`
- Create: `src/TgTui.Telegram/WTelegramMessageService.cs`
- Create: `src/TgTui.Telegram/WTelegramUpdateHub.cs` (complete: hook `client.OnUpdates`)
- Test: `tests/TgTui.Telegram.Tests/TelegramMapperTests.cs` with hand-built TL objects where feasible, or pure mapping DTOs

**Interfaces:**
- Consumes: shared `Client` from session
- Produces: full `IDialogService`, `IMessageService`, live `IUpdateHub`

Mapping rules:
- Dialog title: user full name / chat title / "Deleted account"
- Avatar letter: first letter of title uppercase, `#` for channels if desired
- Last message preview: text or `📷 Photo` / `📎 File` / `Sticker`
- Unread from dialog unread_count
- Message outgoing: `msg.out`
- Media: populate `MediaAttachment` with kind `photo`/`document` and Telegram locator fields — extend `MediaAttachment` with `string? TelegramFileId` or store `long messageId` + chat for download in Media/Telegram bridge

**Download bridge:** add to Core:

```csharp
public interface IMediaDownloader
{
    Task<string> DownloadMessageMediaAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken = default);
}
```

Implement in Telegram project; `MediaService` depends on optional `IMediaDownloader`.

- [ ] **Step 1: Extend MediaAttachment + IMediaDownloader; write mapper tests for preview text**

```csharp
[Theory]
[InlineData(null, null, "")]
[InlineData("hi", null, "hi")]
public void Preview(string? text, string? mediaKind, string expected) { ... }
```

Implement pure functions on `TelegramMapper`:
- `string Preview(string? text, bool hasPhoto, bool hasDocument, string? documentName)`
- `char AvatarLetter(string title)`

- [ ] **Step 2: Implement dialog list via `Messages_GetAllDialogs` / `Messages_Dialogs` iteration (WTelegramClient helper `Messages_GetAllDialogs`)**

Sort: pinned first, then `LastMessageAt` descending.

- [ ] **Step 3: History via `Messages_GetHistory`; send via `SendMessageAsync`; edit `Messages_EditMessage`; delete `Messages_DeleteMessages`**

- [ ] **Step 4: Update hub — on new message / read / dialog refresh fire events**

- [ ] **Step 5: Tests for mapper + any pure sort helpers; build solution**

- [ ] **Step 6: Suggested commit**

```bash
git commit -am "feat(telegram): dialogs, messages, updates mapping"
```

---

### Task 7: Wire MediaService download + protocol renderer stub

**Files:**
- Modify: `src/TgTui.Media/MediaService.cs`
- Create: `src/TgTui.Media/ProtocolImageRenderer.cs`
- Test: `tests/TgTui.Media.Tests/MediaServiceTests.cs`

**Behavior:**
- `EnsureLocalAsync`: if `LocalPath` set and exists, return it; else call `IMediaDownloader` and cache
- `RenderPreview`: detect capability; Kitty/Sixel/ITerm2 → `ProtocolImageRenderer` (v1 may fall back to half-block if protocol encode incomplete, but structure must branch correctly); HalfBlock → half-block; None → `"🖼 (open with o)"`
- Protocol v1 minimum: implement Kitty graphics **or** Sixel for PNG bytes; if time-boxed, implement one protocol fully + half-block, leave second protocol as clear TODO method that falls back — **prefer**: half-block always works; for Kitty write APC sequence with raw image base64 (documented protocol); for Sixel use a small encoder or fall back

Acceptance: capability branch unit-tested with fake detector.

- [ ] **Step 1–4: TDD branch selection + EnsureLocal with fake downloader**
- [ ] **Step 5: Suggested commit** `feat(media): download bridge and render branching`

---

### Task 8: UI theme, keymap constants, help overlay

**Files:**
- Create: `src/TgTui.UI/Theme/TelegramDesktopTheme.cs`
- Create: `src/TgTui.UI/Keymap/KeyBindings.cs`
- Create: `src/TgTui.UI/Views/HelpOverlay.cs`
- Create: `src/TgTui.UI/AppRunner.cs` (init Terminal.Gui, apply theme)

**Theme:** map colors from spec to Terminal.Gui `Color` / `Attribute` scheme for Windows, Dialog list selected, Message bubbles (use labels/views with background).

**KeyBindings:** public const strings for help text (actual binding in views).

Help text content must list keys from design §3.4.

- [ ] **Step 1: Implement theme application method `TelegramDesktopTheme.Apply()`**
- [ ] **Step 2: HelpOverlay content exact key tables**
- [ ] **Step 3: AppRunner starts Application and shows placeholder label "tg-tui" — manual run**

```bash
dotnet run --project src/TgTui
```

- [ ] **Step 4: Suggested commit** `feat(ui): theme, help overlay, app runner shell`

---

### Task 9: Auth wizard view

**Files:**
- Create: `src/TgTui.UI/Views/AuthWizardView.cs`
- Modify: `src/TgTui/Program.cs` DI + run auth then shell

**Behavior:**
- Full-screen view switching on `AuthState.Phase`
- Fields: api_id (int), api_hash, phone, code, password
- Status label for errors / flood
- On `Ready`, invoke callback to open `ChatShellView`

- [ ] **Step 1: Implement wizard UI bound to `IAuthService`**
- [ ] **Step 2: Program.cs builds Host with DI registrations**

```csharp
services.AddSingleton(AppPaths.ForCurrentUser());
services.AddSingleton<ConfigStore>();
services.AddSingleton<IDraftStore, FileDraftStore>(...);
services.AddSingleton<IAuthService, WTelegramAuthService>();
// etc.
```

- [ ] **Step 3: Manual test path documented in CONTRIBUTING later**
- [ ] **Step 4: Suggested commit** `feat(ui): auth wizard`

---

### Task 10: Chat shell — dialog list + message pane + composer

**Files:**
- Create: `Views/ChatShellView.cs`, `DialogListView.cs`, `MessagePaneView.cs`, `ComposerView.cs`, `StatusBarView.cs`
- Create: `ViewModels/*`

**Focus model:** enum `FocusZone { Dialogs, Messages, Composer }` on shell; Tab cycles optional; keys per design.

**DialogListView:**
- `j/k` move, `Enter`/`l` open, `/` open filter TextField, `m` mute toggle, `p` pin toggle
- Render unread badge, muted/pin markers

**MessagePaneView:**
- Vertical list of messages; outgoing right styling; reply quote line; media preview line via `IMediaService.RenderPreview` when local available (async load on select)
- Keys: `j/k`, `r`, `e`, `d`, `o`, `i`/`a`, `g`/`h`, `G`

**ComposerView:**
- Multiline TextView; Enter send (if not shift); Esc cancel reply + blur; load/save drafts via `IDraftStore` on chat switch and on text change (debounced save on blur/send)

**StatusBarView:** connection from `IUpdateHub`

**Threading:** subscribe to hub events; `Application.Invoke` / main thread marshal to refresh.

- [ ] **Step 1: Build layout containers matching §2 mockup proportions**
- [ ] **Step 2: Wire ViewModels to services**
- [ ] **Step 3: Implement key handlers zone by zone**
- [ ] **Step 4: Manual smoke: navigate fake services**

For offline UI dev, add `TgTui.UI` design-time `FakeDialogService` / `FakeMessageService` behind env `TG_TUI_FAKE=1` returning sample dialogs/messages — tests can use fakes too.

- [ ] **Step 5: Suggested commit** `feat(ui): chat shell with keyboard-first panes`

---

### Task 11: Wire live Telegram into shell + reply/edit/delete/mute/pin

**Files:**
- Modify: ViewModels and Telegram services as needed
- Remove gaps from Task 6/10

**Acceptance actions:**
- Opening dialog loads history
- Send appears optimistically then reconciles
- Reply sets `replyToId` on send
- Edit only if `IsOutgoing`
- Delete removes from list on success
- Mute/pin call dialog service and refresh list
- Drafts persist across chat switches

- [ ] **Step 1: Integrate real services (disable fake unless env set)**
- [ ] **Step 2: End-to-end manual checklist from spec §10 chat shell**
- [ ] **Step 3: Suggested commit** `feat: live telegram chat operations`

---

### Task 12: Media in message pane (smart inline)

**Files:**
- Modify: `MessagePaneView.cs`, `MediaService.cs`, protocol renderer

**Behavior:**
- When selected message has media, call `EnsureLocalAsync` then `RenderPreview`
- Show placeholder while loading
- `o` / Enter on media → `OpenExternallyAsync`
- Failures show `🖼 image unavailable` without blocking scroll

- [ ] **Step 1: Loading state + cache**
- [ ] **Step 2: Manual verify half-block in generic terminal; protocol in WezTerm/Kitty if available**
- [ ] **Step 3: Suggested commit** `feat(media): inline previews in message pane`

---

### Task 13: Publish profiles + CI/CD workflows

**Files:**
- Modify: `src/TgTui/TgTui.csproj` publish properties
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/release.yml`
- Create: `.github/dependabot.yml`

**csproj properties:**
```xml
<PropertyGroup>
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <PublishTrimmed>false</PublishTrimmed>
  <AssemblyName>tg-tui</AssemblyName>
  <Version>0.1.0</Version>
</PropertyGroup>
```

**CI (`ci.yml`):**
```yaml
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet test --no-build -c Release --verbosity normal
```

**Release (`release.yml`):** on tag `v*`, matrix RIDs `linux-x64`, `osx-x64`, `osx-arm64`, `win-x64`, `dotnet publish -c Release -r ${{ matrix.rid }} --self-contained true -p:PublishSingleFile=true -o artifacts/${{ matrix.rid }}`, upload-artifact + softprops/action-gh-release.

**Publish smoke (local):**
```bash
dotnet publish src/TgTui -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o publish/osx-arm64
```

- [ ] **Step 1: Add csproj props + workflows**
- [ ] **Step 2: Local publish one RID**
- [ ] **Step 3: Suggested commit** `ci: add build test and multi-rid release workflows`

---

### Task 14: GitHub community docs + polished README

**Files:**
- Create: `README.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `LICENSE`, `CHANGELOG.md`
- Create: `.github/PULL_REQUEST_TEMPLATE.md`, `.github/ISSUE_TEMPLATE/bug_report.md`, `.github/ISSUE_TEMPLATE/feature_request.md`

**README.md requirements (must include):**
1. Title `tg-tui` + one-liner
2. Badges placeholders: build, license MIT, .NET 10
3. ASCII mock of split-pane UI (from design)
4. Features list (daily-driver + smart inline media)
5. Install: release binary + `dotnet run --project src/TgTui`
6. Quick start: my.telegram.org api_id/hash, first login
7. Keyboard tables (list / messages / composer / global)
8. Config paths
9. `dotnet publish` examples per RID
10. Link CONTRIBUTING + LICENSE

**CONTRIBUTING.md:** .NET 10 SDK, `dotnet test`, one-type-per-file, no secrets in PRs, how to run with `TG_TUI_FAKE=1`, PR expectations.

**CODE_OF_CONDUCT.md:** Contributor Covenant 2.1 text.

**SECURITY.md:** report via GitHub private security advisory; never paste session.dat / api_hash.

**LICENSE:** MIT with copyright year 2026 and holder "Igor" or project name as user prefers — use `Copyright (c) 2026 tg-tui contributors` if name unknown.

**CHANGELOG.md:**
```markdown
# Changelog

## [Unreleased]

### Added
- Initial daily-driver Telegram TUI client
```

- [ ] **Step 1: Write all community files**
- [ ] **Step 2: Proofread README against running key bindings**
- [ ] **Step 3: Suggested commit** `docs: add README and GitHub community files`

---

### Task 15: Acceptance pass + polish

**Files:** touch as needed for bugs found

- [ ] **Step 1: Walk design §10 acceptance checkboxes** against a real account (manual)
- [ ] **Step 2: Fix defects; keep tests green**

```bash
dotnet test
dotnet build -c Release
```

- [ ] **Step 3: Ensure `.gitignore` blocks secrets; `git status` clean of session/config**
- [ ] **Step 4: Suggested commit** `chore: v1 acceptance polish`

---

## Spec coverage checklist

| Spec area | Task(s) |
|---|---|
| Solution layout / layers | 1–2 |
| App paths / config TOML | 2, 5 |
| Drafts | 3, 10–11 |
| Dialog filter | 3, 10 |
| Auth wizard + session | 5, 9 |
| Dialogs / messages / updates | 6, 11 |
| Reply/edit/delete/mute/pin | 6, 11 |
| UI layout + theme + keys + help | 8, 10 |
| Smart inline media | 4, 7, 12 |
| Self-contained multi-RID | 13 |
| CI / release / dependabot | 13 |
| README + GitHub community | 14 |
| Tests Core/Media (+ mapper) | 2–7 |
| v1 acceptance | 15 |
| Non-goals excluded | — (no tasks for multi-account, calls, etc.) |

---

## Self-review notes

- No TBD steps; fakes via `TG_TUI_FAKE=1` for UI without network.
- Types aligned: `ChatId`, `MessageId`, ports stable from Task 2.
- Protocol image encoding may fall back to half-block if a protocol encoder is incomplete — structure and capability branching remain required; Kitty or Sixel should have a real implementation path in Task 7/12.
- Commits are **suggested for the user**; agents do not commit by default per project rules.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-15-tg-tui-implementation.md`.

**Two execution options:**

1. **Subagent-Driven (recommended)** — fresh subagent per task, review between tasks, fast iteration  
2. **Inline Execution** — execute tasks in this session with executing-plans and checkpoints  

Which approach?
