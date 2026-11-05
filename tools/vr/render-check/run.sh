#!/bin/bash
# Renders the VR comfort overlay, the VR menu and the wrist displays to PNGs under Xvfb.
# Usage: tools/vr/render-check/run.sh [output dir]      (packages: xvfb libgl1-mesa-dri libsdl2-2.0-0)
set -e
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$(realpath -m "${1:-$DIR/out}")"
WORK="$(mktemp -d)"
Xvfb :98 -screen 0 1280x720x24 > "$WORK/xvfb.log" 2>&1 & XVFB_PID=$!
trap 'kill $XVFB_PID 2>/dev/null || true' EXIT
sleep 1
dotnet build "$DIR/RenderCheck.csproj" -c Release -o "$WORK/bin" -v q -nologo > "$WORK/build.log" || { grep -E "error" "$WORK/build.log" | head -20; exit 1; }
DISPLAY=:98 dotnet "$WORK/bin/RenderCheck.dll" "$OUT"
