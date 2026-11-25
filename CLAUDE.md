# CLAUDE.md — working notes for BlueID

Specification: `PROMPT_BlueID_ClaudeCode.md` (authoritative; work phase by phase, section 13, no skipping).
Decisions: `docs/00-quyet-dinh-ky-thuat.md`. Bugs (including our own): `docs/08-so-loi.md`.
Integration contract: `docs/05-hop-dong-tich-hop.md` — any change to claims, events or APIs goes there first
(MINOR bump, history row, contract test). Never a breaking change inside a major version.

## Ground rules (from the spec)
- OAuth/OIDC core = OpenIddict on ASP.NET Core Identity; crypto only from `System.Security.Cryptography`.
  Own code: directory, tenants, MFA (TOTP, WebAuthn), SCIM, SAML (on `SignedXml`), events, policies, UI, packages.
- PKCE S256 for every client, no implicit/hybrid-front-channel/ROPC, exact redirect URI match.
- No stubs, no TODOs. When a decision is needed: decide (safer option), record it in docs/00, continue (owner's
  instruction 2026-11-25).
- `tenant_id` in every business table; "only one …" rules are DB unique constraints; concurrency tests truly parallel.
- Every directory write through `IDirectoryWriter` (change_log + events.outbox in the same transaction).
- Secrets compared with `CryptographicOperations.FixedTimeEquals`. Never log passwords, OTPs, tokens, secrets.
- Code/comments/commits in English; docs in Vietnamese. Commit per phase `phase-N: …`, push to `origin main`
  (https://github.com/minhhung19872002/BlueID — **public repo**: no production secrets, ever).

## Dev environment
- Docker network `10.78.0.0/24` (BlueMail uses 10.77), project `blueid-dev`; IP/port plan in docs/00 Z1.
- Issuer `https://id.blue.test:54443` via Caddy (alias inside the network, same port on the host).
  Host-run processes: BlueID.Web 5090, BlueID.Api 5091, sample product 5095.
- Conformance suite: prebuilt images `release-v5.3.4` + mongo 6.0.13 (mongo 7 breaks it, BUG-002); its run scripts
  are in `.cache/conformance-suite` (git-ignored clone of the same tag).
- LDAP seed: `python scripts/dev-gen-ldap.py` before the first `up` (generated file is git-ignored).
- Windows/Git Bash: prefix docker exec with absolute container paths with `MSYS_NO_PATHCONV=1`.
- BlueMail (reference for conventions and the embedded identity being replaced): `D:\Source\BlueMail`.

## Layout (Phase 1)
- `src/BlueID.Domain` entities · `Infrastructure` (EF context + migrations, PBKDF2 hasher, Identifiers (NFC/IDNA), audit
  chain, JSON localiser `Localization/Resources/{en,vi}.json`) · `Application` (MediatR features, `IDirectoryWriter`,
  PasswordService/SessionService/SignInService, web plumbing, seeder) · `Web` (Razor sign-in, portal host) · `Api`
  (`/admin/v1`, `/me/v1`). Portal: `web/` (React + AntD), built into `src/BlueID.Web/wwwroot/portal` (git-ignored).
- Migrations: `dotnet ef migrations add <Name> -p src/BlueID.Infrastructure -s src/BlueID.Infrastructure -o Persistence/Migrations`.
  Never edit a committed migration. Unique indexes: explicit `ux_` names, mapped in `DirectoryWriter.SaveUniqueAsync`.
- New endpoint: `[RequirePermission]` or `[SelfGuarded("why")]` (scan test). New error code: `error.<code>` in en+vi.
  New package: list it in docs/09 with the exact version (test). New setting: only together with the code that reads it.
- Stop host-run processes before `dotnet build/test` (they lock bin/): PowerShell
  `Get-CimInstance Win32_Process | ? CommandLine -match 'BlueID\.(Web|Api)' | % { Stop-Process -Id $_.ProcessId -Force }`.
- Portal through Caddy needs one origin: Playwright with `--host-resolver-rules=MAP id.blue.test 127.0.0.1`.
- Long heredocs with many quotes break the shell; never write C# `\n` escapes through Python heredocs (they become real
  newlines). Write scripts to the scratchpad with the Write tool, or use Edit.
- Production trial: `docker compose -p blueid-trial -f deploy/docker-compose.yml --env-file <trial.env> up -d` with
  `BID_HTTPS_PORT=58443`, then `python scripts/dev-smoke-login.py https://localhost:58443`; `down -v` and prune images after.
