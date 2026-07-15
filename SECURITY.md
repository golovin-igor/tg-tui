# Security Policy

## Supported versions

Security fixes target the latest release on the default branch. Pre-1.0 versions may receive fixes on a best-effort basis.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security problems.

Report vulnerabilities using **GitHub private security advisories** for this repository:

1. Open the repository on GitHub
2. **Security** → **Advisories** → **New draft security advisory**  
   (or use the “Report a vulnerability” button if enabled)

Include:

- Affected version / commit if known
- Impact description
- Steps to reproduce (without secrets)
- Suggested fix if you have one

We will acknowledge reports as soon as practical and coordinate disclosure.

## What never to share

**Never paste or attach any of the following** in issues, PRs, advisories, screenshots, or logs:

| Item | Why |
|---|---|
| `session.dat` | Full account session — equivalent to a logged-in client |
| `api_hash` | Application secret from my.telegram.org |
| Login / SMS codes | Account takeover |
| 2FA cloud password | Account takeover |
| Full `config.toml` with secrets | Often contains `api_id` / `api_hash` |

Safe to share (after redaction): OS, terminal emulator, app version, stack traces with secrets stripped, and high-level repro steps.

### Local secret locations

- Linux / macOS: `~/.config/tg-tui/` (`session.dat`, `config.toml`, …)
- Windows: `%AppData%\tg-tui\`

If you believe a session was exposed, revoke / log out other sessions in Telegram settings and treat the compromise as serious.

## Scope notes

tg-tui is a full user MTProto client. Session files and API credentials are highly sensitive. Default logging must not include message bodies, session bytes, `api_hash`, or auth codes.
