# tg-tui

A keyboard-first Telegram client for the terminal — full user MTProto login, split-pane chat, and smart inline media.

[![CI](https://img.shields.io/github/actions/workflow/status/OWNER/tg-tui/ci.yml?branch=main&label=build)](https://github.com/OWNER/tg-tui/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platforms](https://img.shields.io/badge/platform-Linux%20%7C%20macOS%20%7C%20Windows-informational)](#install)

> Replace `OWNER` in the build badge URL with your GitHub user or org once the repo is published.

```
┌─ tg-tui  ·  connected  ·  ? help · q quit · Ctrl+L redraw ──────────────┐
│ Dialogs              │  Alice                                          │
│ / filter…            │  online                                         │
│──────────────────────│─────────────────────────────────────────────────│
│ ★ Team chat     2    │           hey — are we shipping today?          │
│   Alice         ·    │  ┌ photo ────────────────────────────┐          │
│ > Bob           1    │  │  (inline Kitty / Sixel / ▄▀ half) │          │
│   #releases          │  └───────────────────────────────────┘          │
│   Saved Messages     │  sounds good — on it                            │
│                      │─────────────────────────────────────────────────│
│                      │  Reply to Alice · draft saved                   │
│                      │  > _                                            │
├──────────────────────┴─────────────────────────────────────────────────┤
│ connected · j/k move · Enter open · r reply · i compose · ? help       │
└────────────────────────────────────────────────────────────────────────┘
```

Telegram Desktop–inspired dark theme (`#0e1621` / `#17212b` / accent `#5eb5f7`).

## Features

**Daily-driver chat**

- Full user client (MTProto via [WTelegramClient](https://github.com/wiz0u/WTelegramClient)) — not bot-only
- Auth wizard: `api_id` / `api_hash` → phone → code → optional 2FA
- Split-pane shell: dialog list, message history, composer
- Read / send / reply / edit / delete
- Unread badges, mute / pin, per-chat drafts
- Keyboard-first focus model (list · messages · composer)

**Smart inline media**

- On-demand photo download + disk cache
- Kitty / iTerm / Sixel when the terminal supports them
- Half-block fallback everywhere else
- Always open media externally with `o` / `Enter`

**Packaging**

- .NET 10 self-contained single-file binaries for Linux, macOS, and Windows

## Install

### Release binary

Download the archive for your platform from [GitHub Releases](../../releases) (or the project Releases page once published):

| RID | Asset (approx.) |
|---|---|
| `linux-x64` | `tg-tui-linux-x64.tar.gz` |
| `osx-x64` | `tg-tui-osx-x64.tar.gz` |
| `osx-arm64` | `tg-tui-osx-arm64.tar.gz` |
| `win-x64` | `tg-tui-win-x64.zip` |

Extract and run `tg-tui` (or `tg-tui.exe` on Windows).

### From source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/OWNER/tg-tui.git
cd tg-tui
dotnet run --project src/TgTui
```

Offline UI demo (no Telegram network, sample dialogs/messages):

```bash
TG_TUI_FAKE=1 dotnet run --project src/TgTui
# Windows (PowerShell): $env:TG_TUI_FAKE=1; dotnet run --project src/TgTui
```

## Quick start

1. Create an application at [https://my.telegram.org](https://my.telegram.org) and note **api_id** and **api_hash**.
2. Run `tg-tui` (or `dotnet run --project src/TgTui`).
3. On first launch, enter **api_id** and **api_hash** when prompted (stored in local config).
4. Enter your phone number, login code, and cloud password if 2FA is enabled.
5. The chat shell opens after a successful login. Session data is stored locally (see [Configuration](#configuration)).

**Never commit or paste** `session.dat`, `api_hash`, login codes, or passwords. See [SECURITY.md](SECURITY.md).

## Keyboard

Exactly one focus zone receives keys: **dialog list**, **message pane**, or **composer**. Press `?` for the in-app help overlay.

### Dialog list

| Key | Action |
|---|---|
| `j` / `k` or arrows | Move selection |
| `Enter` / `l` | Open chat |
| `/` | Filter dialogs |
| `m` | Mute / unmute |
| `p` | Pin / unpin |

### Message pane

| Key | Action |
|---|---|
| `j` / `k` | Select message |
| `r` | Reply |
| `e` | Edit (own messages) |
| `d` | Delete |
| `o` | Open media externally |
| `Enter` | Expand / open media when on a media message |
| `i` / `a` | Focus composer |
| `g` / `h` | Focus dialog list |
| `G` | Jump to latest |

### Composer

| Key | Action |
|---|---|
| `Enter` | Send |
| `Shift+Enter` | Newline |
| `Esc` | Cancel reply / leave composer |
| *(auto)* | Draft saved per chat |

### Global

| Key | Action |
|---|---|
| `?` | Help overlay |
| `Ctrl+L` | Redraw |
| `q` | Quit (confirm if needed) |

## Configuration

User data root:

| Platform | Path |
|---|---|
| Linux / macOS | `~/.config/tg-tui/` |
| Windows | `%AppData%\tg-tui\` |

| Item | Relative path | Notes |
|---|---|---|
| Config | `config.toml` | Includes `api_id` / `api_hash` — treat as secret |
| Session | `session.dat` | **Secret** — never share or commit |
| Media cache | `media/` | On-demand downloads |
| Drafts | `drafts.json` | Per-chat composer drafts |
| Logs | `logs/` | No message bodies / session / codes by default |

## Building & publishing

Restore, build, and test:

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Self-contained single-file publish (matches CI release workflow):

```bash
# Linux x64
dotnet publish src/TgTui -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64

# macOS Intel
dotnet publish src/TgTui -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -o publish/osx-x64

# macOS Apple Silicon
dotnet publish src/TgTui -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o publish/osx-arm64

# Windows x64
dotnet publish src/TgTui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
```

Binary name: `tg-tui` (`tg-tui.exe` on Windows).

## Stack

- [.NET 10](https://dotnet.microsoft.com/)
- [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) — multi-pane TUI
- [WTelegramClient](https://github.com/wiz0u/WTelegramClient) — pure C# MTProto
- Spectre.Console accents where useful

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE) — Copyright (c) 2026 tg-tui contributors.
