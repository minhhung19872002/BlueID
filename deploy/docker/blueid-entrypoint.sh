#!/bin/bash
# Entry point of the blueid/server image: starts the requested process.
set -eu
case "${1:-}" in
  web) cd /app/web && exec dotnet BlueID.Web.dll ;;
  api) cd /app/api && exec dotnet BlueID.Api.dll ;;
  healthcheck)
    # /health of the process in this container (no curl in the image: bash /dev/tcp).
    exec 3<>/dev/tcp/127.0.0.1/8080 || exit 1
    printf 'GET /health HTTP/1.0\r\nHost: localhost\r\n\r\n' >&3
    head -n 1 <&3 | grep -q ' 200 ' ;;
  *) echo "usage: blueid web|api" >&2; exit 2 ;;
esac
