#!/bin/sh
# Run a BlueID process on the host against the dev containers (Caddy publishes them as https://id.blue.test:54443).
# Usage: scripts/dev-run.sh web|api      (portal dev server: cd web && npm run dev)
# Start web first (it migrates and seeds), then api; two "dotnet run" builds at once lock shared obj/ files.
set -eu
set -a
. ./deploy/.env.dev
set +a
export ASPNETCORE_ENVIRONMENT=Development
case "${1:-}" in
  web) export ASPNETCORE_URLS=http://0.0.0.0:5090; exec dotnet run --no-launch-profile --project src/BlueID.Web ;;
  api) export ASPNETCORE_URLS=http://0.0.0.0:5091; exec dotnet run --no-launch-profile --project src/BlueID.Api ;;
  *) echo "usage: $0 web|api" >&2; exit 2 ;;
esac
