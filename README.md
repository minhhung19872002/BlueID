# BlueID

Identity provider and organisation directory shared by the Blue product suite (BlueMail, BlueChat, BlueDrive,
BlueOffice…). Products talk to BlueID only through open standards (OIDC, OAuth 2.0, SCIM, SET) and the documented
directory API.

- Specification: [`PROMPT_BlueID_ClaudeCode.md`](PROMPT_BlueID_ClaudeCode.md)
- **Integration contract (the only document products need): [`docs/05-hop-dong-tich-hop.md`](docs/05-hop-dong-tich-hop.md)**
- Technical decisions: [`docs/00-quyet-dinh-ky-thuat.md`](docs/00-quyet-dinh-ky-thuat.md)

## Status

| Phase | Content | State |
|---|---|---|
| 0 | Dev infrastructure, reference systems, integration contract 1.0 | ✅ done |
| 1 | Foundation: solution, tenants, users, passwords, Razor sign-in, sessions, audit, i18n, React portal shell | ✅ done |
| 2 | OAuth/OIDC core (OpenIddict), key rotation, logout (RP-initiated, back-channel), consent, sample product; OpenID conformance suite: 5 profiles, 0 failures | ✅ done |
| 3 | MFA: TOTP, passkeys/WebAuthn, email codes, recovery codes, self-service reset, password policy | ⏸ paused, not started |

**Paused on 2026-11-25 after Phase 2.** Resume with Phase 3 (PROMPT section 13). Open items carried forward:
- Phase 5: event `user.sessions_revoked` (reason `refresh_token_reuse`) on refresh-token reuse (docs/00 D15).
- Phase 6: ES256 signing per client; any loopback port for native apps (RFC 8252 7.3).
- OBS-001 (docs/08): one unexplained mass failure of integration tests, not reproduced since.
- Conformance REVIEW screenshots need a human review before an official OpenID certification (docs/07).
- Tests at pause: 35 unit, 54 integration, 3 vitest — all green.

## Dev environment (Phase 0)

Requirements: Docker, .NET SDK 10.0.404, Node 22, Python 3.

```sh
python scripts/dev-gen-ldap.py                     # OpenLDAP seed (10 000 users, git-ignored)
docker compose -f deploy/docker-compose.dev.yml --env-file deploy/.env.dev up -d
sh scripts/dev-phase0-check.sh                     # PHASE 0 CHECK: PASS
```

Add to the hosts file: `127.0.0.1 id.blue.test sample.blue.test idp.doitac.test localhost.emobix.co.uk`.

| Service | URL (host) |
|---|---|
| BlueID (issuer) | https://id.blue.test:54443 |
| Mail catcher (Mailpit) | http://127.0.0.1:54025 |
| Reference IdP (Keycloak, realm `doitac`) | http://idp.doitac.test:54080 |
| OpenLDAP | ldap://127.0.0.1:54389 (`cn=admin,dc=doitac,dc=test`) |
| OpenID conformance suite | https://localhost.emobix.co.uk:8443 |
| PostgreSQL / Valkey | 127.0.0.1:54432 / 127.0.0.1:54379 |

Dev credentials are in `deploy/.env.dev` (dev only, test domains only).

## Run BlueID (Phase 1)

```sh
cd web && npm ci && npm run build && cd ..      # portal → src/BlueID.Web/wwwroot/portal
sh scripts/dev-run.sh web                        # migrates + seeds, http://localhost:5090
sh scripts/dev-run.sh api                        # http://localhost:5091 (start after web)
python scripts/dev-smoke-login.py                # SMOKE: PASS
```

Open https://id.blue.test:54443/ (Caddy, internal CA). Sample accounts: `admin@acme.test` (vi) and
`admin@bluemail.test` (en), password `Sample-User-Pass-2026`; platform `admin@blue.test` / `Platform-Dev-Pass-2026`
(must change at first sign-in). Tests: `dotnet test BlueID.slnx` (Docker needed), `cd web && npx vitest run`.

Production: `deploy/docker-compose.yml` + `deploy/.env.example`; image `deploy/docker/blueid.Dockerfile`.

## OAuth / OIDC (Phase 2)

```sh
sh scripts/dev-run.sh sample                     # sample product on https://sample.blue.test:54443 (after web)
sh scripts/conformance/run.sh [plan-filter]      # OpenID conformance suite, 5 profiles (needs .cache, see CLAUDE.md)
```

Discovery: https://id.blue.test:54443/.well-known/openid-configuration · contract: `docs/05` (1.1).
