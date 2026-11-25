#!/bin/sh
# Run a BlueID process on the host against the dev containers (Caddy publishes them as https://id.blue.test:54443).
# Usage: scripts/dev-run.sh web|api|sample      (portal dev server: cd web && npm run dev)
# Start web first (it migrates and seeds), then api / sample; two "dotnet run" builds at once lock shared obj/ files.
set -eu
set -a
. ./deploy/.env.dev
set +a
export ASPNETCORE_ENVIRONMENT=Development
# dotnet run starts in the project directory: make repository paths absolute (pwd -W: Windows path in Git Bash).
root=$(pwd -W 2>/dev/null || pwd)
export BID_SAMPLE_JWKS_FILE="$root/deploy/dev/sample/sample-product.jwks.json"
export SAMPLE_KEY_FILE="$root/deploy/dev/sample/sample-product.key.pem"
case "${1:-}" in
  web) export ASPNETCORE_URLS=http://0.0.0.0:5090; exec dotnet run --no-launch-profile --project src/BlueID.Web ;;
  api) export ASPNETCORE_URLS=http://0.0.0.0:5091; exec dotnet run --no-launch-profile --project src/BlueID.Api ;;
  sample)
    # Trust Caddy's internal CA for BlueID's certificate (exported from the dev container).
    docker exec blueid-dev-caddy-1 cat /data/caddy/pki/authorities/local/root.crt > "$root/deploy/dev/sample/caddy-root.crt"
    export SAMPLE_CA_FILE="$root/deploy/dev/sample/caddy-root.crt"
    export ASPNETCORE_URLS=http://0.0.0.0:5095; exec dotnet run --no-launch-profile --project samples/SampleProduct ;;
  *) echo "usage: $0 web|api|sample" >&2; exit 2 ;;
esac
