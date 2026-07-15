# tg-tui Design Spec

**Date:** 2026-07-15  
**Status:** Approved (brainstorm sections §1–§5)  
**Product:** Nice-looking Telegram client for the terminal (TUI)

---

## 1. Goals

Build a **full user Telegram client** (MTProto, normal account login) with a **modern, keyboard-first TUI**, packaged as **.NET 10 self-contained** binaries for Windows, macOS, and Linux.

### Product decisions (locked)

| Topic | Decision |
|---|---|
| Identity | Full user client (not bot-only) |
| v1 scope | Daily-driver: auth, dialogs, read/send, reply/edit/delete, unread, mute/pin, drafts, media |
| Images | Smart inline: Sixel/Kitty/iTerm when available → half-block fallback → always open externally |
| Look | Telegram Desktop–inspired default theme (dark blue-gray, accent blue) |
| Stack | Terminal.Gui + WTelegramClient + Spectre.Console accents |
| Runtime | .NET 10, self-contained, cross-platform |
| License | MIT |

### Non-goals (v1)

- Multi-account
- Chat folders / filters UI
- Global message search
- Voice/video calls
- Full sticker pack browser / animated sticker playback
- Bot mode
- Perfect pixel parity with Telegram Desktop
- Homebrew / Scoop / AUR (after first release)
- Dedicated docs site

---

## 2. Architecture

### 2.1 Approach

**Approach 1 (chosen):** Terminal.Gui for multi-pane full-screen UI and focus/key bindings; WTelegramClient for pure C# MTProto (no native TDLib); Spectre.Console for markup polish and half-block image rendering helpers where useful.

### 2.2 Solution layout

```
tg-tui/
├── src/
│   ├── TgTui/                 # Console host — Program, DI, publish profiles
│   ├── TgTui.Core/            # Domain models, ports (interfaces), pure logic
│   ├── TgTui.Telegram/        # WTelegramClient gateway, session, updates
│   ├── TgTui.Media/           # Download, terminal capability detect, render
│   └── TgTui.UI/              # Terminal.Gui views, theme, keymaps
├── tests/
│   ├── TgTui.Core.Tests/
│   ├── TgTui.Telegram.Tests/  # Fakes / contract tests where possible
│   └── TgTui.Media.Tests/
├── docs/superpowers/specs/    # Design specs
├── .github/                   # CI, release, issue/PR templates
├── README.md
├── CONTRIBUTING.md
├── CODE_OF_CONDUCT.md
├── SECURITY.md
├── LICENSE
├── CHANGELOG.md
├── global.json
└── .editorconfig
```

### 2.3 Dependency rules

- **One type per file**; file name matches type name.
- Reference direction: `UI → Core ← Telegram / Media`. Host wires DI.
- UI never references WTelegramClient types directly.
- SOLID / DRY / SRP; search for existing helpers before adding new ones.

### 2.4 Runtime layers

| Layer | Responsibility |
|---|---|
| Host | Config, DI, logging, graceful shutdown |
| UI | Panes, focus, key bindings, Telegram Desktop–inspired theme, view-models |
| Core services | Dialogs, messages, drafts, mute/pin, unread, commands |
| Telegram gateway | Auth, session file, API, update stream → domain events |
| Media pipeline | On-demand download, disk cache, capability detect, inline/external |

### 2.5 Threading

- Telegram network/updates on background tasks.
- All UI mutations marshaled to the Terminal.Gui main loop.
- No blocking API calls on the UI thread.

---

## 3. UI layout & keyboard UX

### 3.1 Screens

1. **Auth wizard** (full-screen): api_id/api_hash (first run) → phone → code → optional 2FA.
2. **Chat shell** (main): split panes after successful login.

### 3.2 Chat shell layout

| Region | Content |
|---|---|
| Left (~30–35%) | Dialog list: avatar initial, title, last preview, time, unread badge; filter bar |
| Right (flex) | Header (name + status) → messages (incoming left / outgoing right) → composer |
| Top chrome | Account / connection status + compact key hints |
| Bottom chrome | Connection state + contextual shortcuts |

### 3.3 Focus model

Exactly one focus zone receives keys:

1. **Dialog list**
2. **Message pane**
3. **Composer**

### 3.4 Default key bindings

**Dialog list**

| Key | Action |
|---|---|
| `j` / `k` or arrows | Move selection |
| `Enter` / `l` | Open chat |
| `/` | Filter dialogs |
| `m` | Mute / unmute |
| `p` | Pin / unpin |

**Message pane**

| Key | Action |
|---|---|
| `j` / `k` | Select message |
| `r` | Reply |
| `e` | Edit (own messages) |
| `d` | Delete |
| `o` | Open media externally |
| `Enter` | Expand / open media when on media message |
| `i` / `a` | Focus composer |
| `g` / `h` | Focus dialog list |
| `G` | Jump to latest |

**Composer**

| Key | Action |
|---|---|
| `Enter` | Send |
| `Shift+Enter` | Newline |
| `Esc` | Cancel reply / leave composer |
| (auto) | Draft saved per chat |

**Global**

| Key | Action |
|---|---|
| `?` | Help overlay |
| `Ctrl+L` | Redraw |
| `q` | Quit (confirm if needed) |

### 3.5 Theme (default)

Telegram Desktop–inspired dark palette:

| Role | Approx. color |
|---|---|
| Deep background | `#0e1621` |
| Panel background | `#17212b` |
| Selection / outgoing bubble | `#2b5278` |
| Accent / unread / links | `#5eb5f7` |
| Muted text | `#6d7f8f` / `#8b9bab` |
| Primary text | `#e4ecf2` |

Incoming bubbles left-aligned; outgoing right-aligned with read receipts when available (`✓` / `✓✓`).

---

## 4. Auth, session, config

### 4.1 Auth flow

1. If no `api_id`/`api_hash` in config → prompt and save.
2. Phone number → request code.
3. Code entry (paste-friendly).
4. Cloud password (2FA) if required.
5. Persist session; enter chat shell.

Inline errors for flood wait, invalid code, network; allow retry without process exit.

### 4.2 Paths

Cross-platform user data root:

- Linux/macOS: `~/.config/tg-tui/`
- Windows: `%AppData%\tg-tui\`

| Item | Path under root |
|---|---|
| Config | `config.toml` |
| Session | `session.dat` (secret) |
| Media cache | `cache/media/` |
| Drafts | `drafts.json` |
| Logs | `logs/tg-tui-*.log` |

Logs must not include message bodies, session bytes, api_hash, or codes by default.

### 4.3 Secrets hygiene

- `.gitignore` excludes config with secrets, session, cache, local publish output, `.superpowers/`.
- Docs and issue templates warn against pasting session/api_hash.

---

## 5. Core services & data flow

### 5.1 Ports (Core)

| Port | Responsibility |
|---|---|
| `IAuthService` | Login steps, auth state |
| `IDialogService` | Load/refresh dialogs, mute, pin, list filter |
| `IMessageService` | History window, send, reply, edit, delete, unread |
| `IDraftStore` | Per-peer draft load/save |
| `IMediaService` | Download, cache, external open, render request |
| `IUpdateHub` | Subscribe to domain events from Telegram updates |

### 5.2 Data flow

```
WTelegramClient updates
        │
        ▼
 Telegram gateway  ──maps TL──►  domain events
        │                              │
        ▼                              ▼
  App services (Core)  ◄──────  UI view-models / commands
        │
        ▼
  Marshal to Terminal.Gui main loop → re-render
```

- **Outgoing messages:** command → service → gateway; optimistic UI where safe; reconcile on ack/update.
- **History:** initial recent page; scroll-up loads older pages.
- **Dialogs:** refresh from updates (new message, read state, pin/mute).

### 5.3 Error handling

| Class | Behavior |
|---|---|
| Transient network | Status “reconnecting…”, auto-retry |
| API / flood | Status or toast; flood wait countdown when applicable |
| Invalid session | Return to auth wizard with explanation |
| Unhandled | Log + friendly modal; do not corrupt session file |

---

## 6. Media pipeline

```
Photo / image document on message
        → on-demand if selected or near viewport
        → download to cache/media/{hash}
        → detect terminal graphics capability
            → Kitty / iTerm / Sixel: protocol inline
            → else: half-block downscaled preview
        → always: `o` / Enter opens via OS handler
```

| Rule | Detail |
|---|---|
| On-demand | No bulk download of entire history media |
| v1 types | Photos + common image docs (jpeg/png/webp/gif still); other files = label + open |
| Decode cap | Limit pixel dimensions / cell width (~40–80 cells); huge files → external only |
| Failures | Placeholder + retry; must not block scrolling |
| Stickers | Image-backed stickers use same path; animated → static frame or placeholder |

---

## 7. Packaging & deployment

### 7.1 Publish

Self-contained .NET 10 publish for at least:

- `linux-x64`
- `osx-x64`
- `osx-arm64`
- `win-x64`

Prefer single-file or single-directory per RID suitable for GitHub Release assets. Document exact `dotnet publish` flags in README.

### 7.2 CI / release

- **CI:** restore, build, test on push/PR (Ubuntu required; additional OS matrix optional). No live Telegram credentials in CI.
- **Release:** on tag `v*`, publish RIDs and attach to GitHub Release.

---

## 8. GitHub & documentation package

### 8.1 Root docs

| File | Purpose |
|---|---|
| `README.md` | Polished landing: badges, ASCII TUI preview, features, install, quick start, keys, config, build/publish, links |
| `CONTRIBUTING.md` | .NET 10 setup, branch/PR, coding rules, tests, secrets policy |
| `CODE_OF_CONDUCT.md` | Contributor Covenant |
| `SECURITY.md` | Private vulnerability reporting; session/credential sensitivity |
| `LICENSE` | MIT |
| `CHANGELOG.md` | Keep a Changelog; start with Unreleased |
| `.gitignore` | .NET + secrets/cache/session + `.superpowers/` |
| `.editorconfig` | C# conventions |
| `global.json` | SDK pin to .NET 10 band |

### 8.2 `.github/`

| Path | Purpose |
|---|---|
| `ISSUE_TEMPLATE/bug_report.md` | OS, terminal emulator, version, repro, redacted logs |
| `ISSUE_TEMPLATE/feature_request.md` | Problem / proposal |
| `PULL_REQUEST_TEMPLATE.md` | Summary, test plan, checklist |
| `workflows/ci.yml` | Build + test |
| `workflows/release.yml` | Multi-RID self-contained artifacts |
| `dependabot.yml` | NuGet + Actions |

### 8.3 README structure

1. Name + one-liner  
2. Badges (build, license, .NET, platforms)  
3. ASCII layout preview (until real screenshots)  
4. Features  
5. Install (binary + from source)  
6. Quick start (api_id/hash, login)  
7. Keyboard map  
8. Configuration paths  
9. Building & publishing  
10. Contributing / License  

---

## 9. Testing strategy

| Area | Approach |
|---|---|
| Core | Unit tests: dialog filter, drafts, message order, unread |
| Media | Unit tests: capability detection, cache keys, half-block path guards |
| Telegram | Gateway behind interfaces; fake update stream for service tests |
| CI | No network calls to Telegram; no real session files |

---

## 10. v1 acceptance criteria

### Auth & session

- [ ] First-run api_id/hash → phone → code → optional 2FA
- [ ] Cold start reuses session
- [ ] Secrets only under user config dir

### Chat shell

- [ ] Dialog list with preview, time, unread; `/` filter
- [ ] Mute and pin from list
- [ ] Open chat; scroll; load older history
- [ ] Send text; receive live updates
- [ ] Reply, edit, delete
- [ ] Per-chat drafts across focus/chat switches
- [ ] §3 key model; `?` help overlay

### Media

- [ ] Smart inline (protocol → half-block → external)
- [ ] Disk cache; external open works on Win/macOS/Linux

### Ship

- [ ] Self-contained publish for four RIDs above
- [ ] Core (+ Media) tests green in CI
- [ ] Full GitHub docs package from §8
- [ ] Default Telegram Desktop–inspired theme

---

## 11. Implementation order (high level)

1. Solution scaffold, `global.json`, editorconfig, gitignore, empty projects, CI stub  
2. Core ports + models + draft store tests  
3. Telegram gateway: config, auth, session  
4. UI shell: theme, three-pane layout, key bindings, help  
5. Wire dialogs + messages + live updates  
6. Reply/edit/delete, mute/pin, drafts  
7. Media pipeline + smart inline  
8. Auth wizard polish, status/errors  
9. Publish profiles + release workflow  
10. README and remaining GitHub community files  

Detailed task breakdown belongs in the implementation plan (next step after spec approval).

---

## 12. Open points (explicit defaults)

| Topic | Default chosen |
|---|---|
| License | MIT |
| Config format | TOML |
| Multi-account | Out of scope v1 |
| Theme pack system | Single default theme v1; structure allows later packs |
| Single-instance lock | Optional later; not required for v1 acceptance |

No unresolved TBDs that block implementation planning.
