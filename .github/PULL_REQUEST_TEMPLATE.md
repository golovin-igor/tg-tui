## Summary

<!-- What does this PR change and why? -->

## Test plan

- [ ] `dotnet test` passes
- [ ] UI smoke with `TG_TUI_FAKE=1` (if UI / host touched)
- [ ] Live Telegram check (only if auth/network path changed; **do not** paste secrets)

## Checklist

- [ ] One type per file; naming matches existing conventions
- [ ] No secrets (`session.dat`, `api_hash`, codes, real `config.toml`)
- [ ] No `bin/` / `obj/` / `publish/` artifacts
- [ ] Docs / CHANGELOG updated if user-facing behavior changed
