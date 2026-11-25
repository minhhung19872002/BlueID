#!/bin/sh
# Phase 0 acceptance: every dev / reference service answers the way later phases rely on.
# Usage: sh scripts/dev-phase0-check.sh   (dev stack up, see README)
set -u
export MSYS_NO_PATHCONV=1
. ./deploy/.env.dev
C="docker compose -f deploy/docker-compose.dev.yml --env-file deploy/.env.dev"
fail=0
ok() { echo "ok   $1"; }
bad() { echo "FAIL $1"; fail=1; }

# PostgreSQL 16 with the extensions migrations expect.
v=$($C exec -T postgres psql -U "$BID_DB_USER" -d "$BID_DB_NAME" -tAc "select current_setting('server_version_num')::int/10000 || ':' || string_agg(extname, ',' order by extname) from pg_extension where extname in ('citext','pg_trgm')" 2>/dev/null)
[ "$v" = "16:citext,pg_trgm" ] && ok "postgres $v" || bad "postgres ($v)"

# Valkey.
[ "$($C exec -T valkey valkey-cli ping 2>/dev/null | tr -d '\r')" = "PONG" ] && ok "valkey" || bad "valkey"

# Mail catcher: SMTP in, HTTP API out.
python - "$BID_DEV_MAIL_SMTP_PORT" "$BID_DEV_MAIL_UI_PORT" <<'EOF' && ok "mailpit smtp -> api" || bad "mailpit"
import json, smtplib, sys, time, urllib.request, uuid
from email.message import EmailMessage
smtp_port, ui_port = int(sys.argv[1]), int(sys.argv[2])
tag = uuid.uuid4().hex
m = EmailMessage(); m["From"] = "noreply@id.blue.test"; m["To"] = "lan@acme.test"; m["Subject"] = f"phase0 {tag}"
m.set_content("Đặt lại mật khẩu / reset")
with smtplib.SMTP("127.0.0.1", smtp_port, timeout=10) as s: s.send_message(m)
for _ in range(20):
    data = json.load(urllib.request.urlopen(f"http://127.0.0.1:{ui_port}/api/v1/search?query=subject:{tag}", timeout=5))
    if data.get("messages_count", 0) == 1: sys.exit(0)
    time.sleep(0.5)
sys.exit(1)
EOF

# Keycloak reference IdP: OIDC discovery and SAML metadata of realm doitac (wait for start-up).
kc=""
for i in $(seq 1 60); do
  kc=$(curl -s "http://127.0.0.1:$BID_DEV_KEYCLOAK_PORT/realms/doitac/.well-known/openid-configuration" | python -c "import sys,json;print(json.load(sys.stdin)['issuer'])" 2>/dev/null) && break
  sleep 3
done
[ "$kc" = "http://idp.doitac.test:$BID_DEV_KEYCLOAK_PORT/realms/doitac" ] && ok "keycloak oidc issuer $kc" || bad "keycloak oidc ($kc)"
curl -s "http://127.0.0.1:$BID_DEV_KEYCLOAK_PORT/realms/doitac/protocol/saml/descriptor" | grep -q "IDPSSODescriptor" && ok "keycloak saml metadata" || bad "keycloak saml metadata"

# OpenLDAP: generated users and groups present (bootstrap of 10 000 entries takes a while on first start).
n=0
for i in $(seq 1 60); do
  n=$($C exec -T openldap ldapsearch -x -LLL -H ldap://localhost -b ou=people,dc=doitac,dc=test -D cn=admin,dc=doitac,dc=test -w "$BID_LDAP_ADMIN_PASSWORD" -z 0 "(objectClass=inetOrgPerson)" dn 2>/dev/null | grep -c '^dn:')
  [ "$n" -ge "$BID_LDAP_USERS" ] && break
  sleep 5
done
[ "$n" -eq "$BID_LDAP_USERS" ] && ok "openldap $n users" || bad "openldap ($n users, want $BID_LDAP_USERS)"
g=$($C exec -T openldap ldapsearch -x -LLL -H ldap://localhost -b ou=groups,dc=doitac,dc=test -D cn=admin,dc=doitac,dc=test -w "$BID_LDAP_ADMIN_PASSWORD" "(objectClass=groupOfNames)" dn 2>/dev/null | grep -c '^dn:')
[ "$g" -eq 8 ] && ok "openldap $g groups" || bad "openldap groups ($g)"

# Caddy: TLS for id.blue.test from the internal CA, reachable by name inside the network.
code=$(curl -sk -o /dev/null -w '%{http_code}' --resolve "id.blue.test:$BID_DEV_HTTPS_PORT:127.0.0.1" "https://id.blue.test:$BID_DEV_HTTPS_PORT/health")
case "$code" in 200|502) ok "caddy https id.blue.test ($code)";; *) bad "caddy ($code)";; esac
$C exec -T conformance-server sh -c "getent hosts id.blue.test" | grep -q 10.78.0.20 && ok "id.blue.test resolves inside the network" || bad "id.blue.test name inside the network"

# OpenID conformance suite UI and API.
code=""
for i in $(seq 1 60); do
  code=$(curl -sk -o /dev/null -w '%{http_code}' "https://localhost.emobix.co.uk:8443/api/plan/available" --resolve localhost.emobix.co.uk:8443:127.0.0.1)
  [ "$code" = "200" ] && break
  sleep 3
done
[ "$code" = "200" ] && ok "openid conformance suite api" || bad "openid conformance suite ($code)"
curl -sk "https://localhost.emobix.co.uk:8443/api/plan/available" --resolve localhost.emobix.co.uk:8443:127.0.0.1 \
  | python -c "import sys,json;names={p['planName'] for p in json.load(sys.stdin)};need={'oidcc-basic-certification-test-plan','oidcc-config-certification-test-plan','oidcc-formpost-basic-certification-test-plan','oidcc-rp-initiated-logout-certification-test-plan','oidcc-backchannel-rp-initiated-logout-certification-test-plan'};missing=need-names;print('missing',missing) if missing else None;sys.exit(1 if missing else 0)" \
  && ok "conformance plans for Basic/Config/Form Post/RP-Initiated/Back-Channel Logout" || bad "conformance plans"

[ $fail -eq 0 ] && echo "PHASE 0 CHECK: PASS" || echo "PHASE 0 CHECK: FAIL"
exit $fail
