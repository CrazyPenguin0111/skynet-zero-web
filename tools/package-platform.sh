#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
platform_rid="${1:-linux-x64}"
case "$platform_rid" in linux-x64|linux-arm64) ;; *) echo 'Use linux-x64 or linux-arm64.' >&2; exit 2;; esac
platform_version="$(git rev-parse --short HEAD)"
platform_output="artifacts/platform-$platform_rid"
python3 tools/verify-bundle.py
# A fresh publish avoids retaining old fingerprinted WebAssembly versions.
python3 - "$platform_output" <<'PY'
import shutil, sys
from pathlib import Path
for directory in (Path('artifacts/publish'), Path(sys.argv[1])):
    if directory.exists():
        shutil.rmtree(directory)
PY
dotnet publish engine/SkynetZero.Web.csproj -c Release -o artifacts/publish
dotnet publish server/SkynetZero.Server.csproj -c Release -r "$platform_rid" --self-contained true -p:UseAppHost=true -o "$platform_output"
mkdir -p "$platform_output/wwwroot" "$platform_output/deploy"
cp -a artifacts/publish/wwwroot/. "$platform_output/wwwroot/"
cp deploy/skynetzero.service deploy/Caddyfile deploy/platform.env.example "$platform_output/deploy/"
cp LICENSE THIRD_PARTY_NOTICES.md "$platform_output/"
cp server/LEAGUE-BOT-LICENSE.txt "$platform_output/"
cp tools/backup-platform.py "$platform_output/deploy/"
printf '%s\n' "$platform_version" > "$platform_output/REVISION"
tar -C "$platform_output" -czf "artifacts/skynetzero-$platform_rid-$platform_version.tar.gz" .
sha256sum "artifacts/skynetzero-$platform_rid-$platform_version.tar.gz"
