#!/bin/sh
# Runs the OpenID conformance suite (dev compose) against BlueID for the five profiles of PROMPT 7.
# Needs: dev compose up, `scripts/dev-run.sh web` (BID_CONFORMANCE_MODE=true is in deploy/.env.dev, docs/00 D14),
# the suite checkout in .cache/conformance-suite (same tag as the image) and its venv (.cache/venv: httpx, pyparsing).
# Usage: sh scripts/conformance/run.sh [plan-filter]
set -eu
root=$(cd "$(dirname "$0")/../.." && pwd)
python "$root/scripts/conformance/make-config.py" >/dev/null
# Relative to the suite's scripts/ directory: run-test-plan.py splits arguments on ":" (Windows drive letters).
cfg=../../../tests/interop/out/blueid-oidcc.json
export CONFORMANCE_SERVER=https://localhost.emobix.co.uk:8443/
export CONFORMANCE_SERVER_MTLS=https://localhost.emobix.co.uk:8444/
export CONFORMANCE_DEV_MODE=1
py="$root/.cache/venv/Scripts/python"
[ -x "$py" ] || py="$root/.cache/venv/bin/python"
out="$root/tests/interop/out/conformance"
mkdir -p "$out"
plans="oidcc-basic-certification-test-plan[server_metadata=discovery][client_registration=static_client]
oidcc-config-certification-test-plan
oidcc-formpost-basic-certification-test-plan[server_metadata=discovery][client_registration=static_client]
oidcc-rp-initiated-logout-certification-test-plan[response_type=code][client_registration=static_client]
oidcc-backchannel-rp-initiated-logout-certification-test-plan[response_type=code][client_registration=static_client]"
args=""
for p in $plans; do
  c=$cfg
  case "$p" in oidcc-backchannel*) c=../../../tests/interop/out/blueid-oidcc-bcl.json ;; esac
  case "$p" in *"${1:-}"*) args="$args $p $c" ;; esac
done
cd "$root/.cache/conformance-suite/scripts"
# shellcheck disable=SC2086
exec "$py" run-test-plan.py --export-dir "$out" --verbose $args
