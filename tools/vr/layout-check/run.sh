#!/bin/bash
# Verifies the hand written OpenXR struct layouts in Simitone.VR against the Khronos C headers.
# Requires: gcc, libopenxr-dev, libx11-dev, libgl-dev, libegl-dev (Debian/Ubuntu package names).
set -e
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$(mktemp -d)"
gcc -o "$OUT/layout" "$DIR/layout.c"
dotnet build "$DIR/LayoutCheck.csproj" -c Release -o "$OUT/cs" -v q -nologo > "$OUT/build.log" || { cat "$OUT/build.log"; exit 1; }
"$OUT/layout" | dotnet "$OUT/cs/LayoutCheck.dll"
