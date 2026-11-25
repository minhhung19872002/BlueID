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
| 1 | Foundation: solution, tenants, users, passwords, Razor sign-in, sessions, audit, i18n, React admin shell | next |

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
