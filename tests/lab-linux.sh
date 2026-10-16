#!/bin/sh
# Builds the Linux app and runs the headless E2E in the lab (Docker network "mrlab", server container "mr-lnx").
set -e
cd "$(dirname "$0")/.."
OUT=${OUT:-/tmp/mr-e2e}
mkdir -p "$OUT"
dotnet publish src/MagicRemote -c Release -f net10.0 -r linux-x64 --self-contained false -o "$OUT/dist" >/dev/null
# a "newer release" for the auto-update test
dotnet publish src/MagicRemote -c Release -f net10.0 -r linux-x64 --self-contained false -p:Version=9.9.9 -o "$OUT/v999/MagicRemote" >/dev/null
tar -czf "$OUT/update.tar.gz" -C "$OUT/v999" MagicRemote
docker run --rm --network mrlab -e ONLY -e WIN_DC -e WIN_USER -e WIN_PASS -v "$OUT/dist:/dist:ro" -v "$PWD/tests:/tests:ro" -v "$OUT:/out" -v "$HOME/.dotnet:/opt/dotnet:ro" \
  mr-lab-client sh /tests/run-linux.sh
