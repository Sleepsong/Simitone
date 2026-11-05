#!/bin/bash
# Renders the VR panels with Simitone's real UI to PNGs, under Xvfb. Needs a published Simitone build (for its
# Content folder; The Sims is not needed).
# Usage: tools/vr/ui-check/run.sh <published Simitone dir> [output dir]
#   e.g. dotnet publish Client/Simitone/Simitone.Desktop -c Release -r linux-x64 --self-contained false -o /tmp/simitone
set -e
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SIMITONE="$(realpath "${1:?published Simitone directory}")"
OUT="$(realpath -m "${2:-$DIR/out}")"
WORK="$(mktemp -d)"
Xvfb :95 -screen 0 1280x720x24 > "$WORK/xvfb.log" 2>&1 & XVFB_PID=$!
trap 'kill $XVFB_PID 2>/dev/null || true' EXIT
sleep 1
dotnet build "$DIR/UICheck.csproj" -c Release -o "$WORK/bin" -v q -nologo > "$WORK/build.log" || { grep -E "error" "$WORK/build.log" | head -20; exit 1; }
DISPLAY=:95 dotnet "$WORK/bin/UICheck.dll" "$SIMITONE" "$OUT"
