# Contributing to tg-tui

Thanks for helping improve tg-tui. This document covers local setup, coding rules, and pull request expectations.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json` for the pinned band)
- A terminal emulator you care about (Kitty, iTerm2, Windows Terminal, etc.) for UI work
- Optional: Telegram **api_id** / **api_hash** from [my.telegram.org](https://my.telegram.org) for live login testing

## Build & test

```bash
dotnet restore
dotnet build
dotnet test
```

Release configuration:

```bash
dotnet build -c Release
dotnet test -c Release
```

## Run locally

Live Telegram client:

```bash
dotnet run --project src/TgTui
```

**Offline UI demo** (no network, fake dialogs/messages/media):

```bash
TG_TUI_FAKE=1 dotnet run --project src/TgTui
```

Windows PowerShell:

```powershell
$env:TG_TUI_FAKE = "1"
dotnet run --project src/TgTui
```

Prefer `TG_TUI_FAKE=1` for UI and keymap work so you never need credentials in development.

## Project layout

| Project | Role |
|---|---|
| `src/TgTui` | Console host, DI, publish settings |
| `src/TgTui.Core` | Domain models, ports, pure logic |
| `src/TgTui.Telegram` | WTelegramClient gateway |
| `src/TgTui.Media` | Download, cache, terminal image render |
| `src/TgTui.UI` | Terminal.Gui views, theme, keymaps |
| `tests/*` | xUnit tests |

Dependency direction: **UI → Core ← Telegram / Media**. The host wires everything. UI must not reference WTelegramClient types directly.

## Coding rules

- **One type per file** — class, interface, enum, or record; file name matches type name
- **SOLID / DRY / SRP** — search the codebase for existing helpers before adding new ones
- Target framework: `net10.0`
- Keep secrets out of source: no `session.dat`, `api_hash`, phone codes, or real config with credentials in PRs
- Do not commit paths ignored by `.gitignore` (`session.dat`, `config.toml`, `media/`, `cache/`, `logs/`, `drafts.json`, local `publish/`)

## Secrets & security

- Never paste `session.dat`, `api_hash`, login codes, or passwords into issues, PRs, or logs
- Redact logs before sharing
- Report vulnerabilities privately — see [SECURITY.md](SECURITY.md)

## Pull requests

1. Keep changes focused; prefer small PRs over large mixed ones
2. Ensure `dotnet test` passes locally
3. Follow the PR template checklist
4. Describe *what* changed and *why*, plus how you tested (fake mode vs live)
5. Do not include generated `bin/` / `obj/` / publish output
6. New public behavior should include or update tests when practical

### Suggested workflow

```bash
# feature branch off main
dotnet test
TG_TUI_FAKE=1 dotnet run --project src/TgTui   # smoke the UI if you touched it
# open PR against main
```

## Code of conduct

By participating you agree to uphold our [Code of Conduct](CODE_OF_CONDUCT.md).

## License

Contributions are accepted under the [MIT License](LICENSE).
