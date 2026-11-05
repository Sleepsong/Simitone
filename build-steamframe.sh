#!/bin/bash
# Builds Simitone with native OpenXR VR for the Valve Steam Frame (SteamOS, linux-arm64).
#
# Usage: ./build-steamframe.sh [debug|release] [--x64] [--native-dir DIR] [--tar]
#
#   --x64             build a linux-x64 package instead (desktop Linux + SteamVR, or Steam Link streaming)
#   --native-dir DIR  also bundle native libraries from DIR (e.g. arm64 libSDL2-2.0.so.0, libopenal.so,
#                     libopenxr_loader.so.1). Not needed on SteamOS: it ships SDL2 and OpenAL, and Simitone
#                     talks to SteamVR's OpenXR runtime directly when no OpenXR loader is installed.
#   --tar             also create a .tar.gz of the package
#
# Build on Linux (or WSL) with the .NET 9 SDK or newer. Cross-compiling for arm64 needs no extra tools:
# only managed code is compiled, and the arm64 .NET runtime is downloaded from NuGet by `dotnet publish`.
# See VR.md for deployment to the headset.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

CONFIG="Release"
ARCH="arm64"
NATIVE_DIR=""
MAKE_TAR=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        debug|Debug) CONFIG="Debug" ;;
        release|Release) CONFIG="Release" ;;
        --x64) ARCH="x64" ;;
        --native-dir) NATIVE_DIR="$2"; shift ;;
        --tar) MAKE_TAR=1 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
    shift
done

RID="linux-$ARCH"
if [[ "$ARCH" == "arm64" ]]; then
    PACKAGE="Simitone-SteamFrame-arm64"
else
    PACKAGE="Simitone-VR-Linux-x64"
fi
TEMP_DIR="publish/tmp-$RID-vr"
FINAL_DIR="publish/$PACKAGE"

echo "========================================"
echo "  Simitone VR build ($RID, $CONFIG)"
echo "========================================"

if ! command -v dotnet &> /dev/null; then
    echo "ERROR: .NET SDK not found. Install the .NET 9 SDK (or newer): https://dotnet.microsoft.com/download"
    exit 1
fi
DOTNET_MAJOR=$(dotnet --version | cut -d'.' -f1)
if [[ "$DOTNET_MAJOR" -lt 9 ]]; then
    echo "ERROR: .NET SDK 9.0 or newer required (found $(dotnet --version))."
    exit 1
fi
echo "Using .NET SDK $(dotnet --version)"

if [[ ! -f "FreeSO/TSOClient/tso.client/FSO.Client.csproj" ]]; then
    echo "Initialising git submodules (FreeSO)..."
    git submodule update --init --recursive
fi

WARN_FLAGS=(/p:TreatWarningsAsErrors=false /p:WarningsAsErrors="")

echo ""
echo "Step 1: restore for $RID..."
dotnet restore Client/Simitone/Simitone.Desktop/Simitone.Desktop.csproj -r "$RID" "${WARN_FLAGS[@]}"

echo ""
echo "Step 2: publish self-contained $RID..."
rm -rf "$TEMP_DIR" "$FINAL_DIR"
dotnet publish Client/Simitone/Simitone.Desktop/Simitone.Desktop.csproj \
    -c "$CONFIG" -r "$RID" --self-contained true --no-restore \
    -o "$TEMP_DIR" "${WARN_FLAGS[@]}"

echo ""
echo "Step 3: assemble $FINAL_DIR..."
mkdir -p "$FINAL_DIR/lib"
mv "$TEMP_DIR"/* "$FINAL_DIR/lib/"
rm -rf "$TEMP_DIR"

# Same content pruning as build-mac-linux.sh: TSO-only data and other platforms' natives.
for d in Content/Objects Content/Patch Content/Avatar Content/MeshReplace Content/iOS Content/Cities \
         Content/Blueprints DatabaseScripts MailTemplates IDERes; do
    rm -rf "$FINAL_DIR/lib/$d"
done
for locale in ru ja fr de it pl ko es pt-BR tr cs zh-Hans zh-Hant sv ro hu; do
    rm -rf "$FINAL_DIR/lib/$locale"
done
if [ -d "$FINAL_DIR/lib/runtimes" ]; then
    for d in "$FINAL_DIR/lib/runtimes/"*/; do
        name="$(basename "$d")"
        [[ "$name" == linux* || "$name" == unix* ]] || rm -rf "$d"
    done
fi

# On arm64, any x64 native library that slipped into the output (e.g. from a package without arm64 builds)
# cannot be loaded and would only shadow the system copy. Remove it.
if [[ "$ARCH" == "arm64" ]] && command -v file &> /dev/null; then
    while IFS= read -r -d '' so; do
        if file -L "$so" | grep -q "x86-64"; then
            echo "  removing x86-64 native library from arm64 package: ${so#$FINAL_DIR/}"
            rm -f "$so"
        fi
    done < <(find "$FINAL_DIR/lib" -name "*.so*" -print0)
fi

if [[ -n "$NATIVE_DIR" ]]; then
    echo "  bundling native libraries from $NATIVE_DIR"
    cp -L "$NATIVE_DIR"/*.so* "$FINAL_DIR/lib/"
fi

cp Client/Simitone/Simitone.VR/SteamFrame/Simitone-VR.sh "$FINAL_DIR/Simitone-VR"
chmod +x "$FINAL_DIR/Simitone-VR" "$FINAL_DIR/lib/Simitone"
if [[ "$ARCH" == "arm64" ]]; then
    cp Client/Simitone/Simitone.VR/SteamFrame/vr.defaults.ini "$FINAL_DIR/lib/vr.defaults.ini"
fi
cp VR.md "$FINAL_DIR/README-VR.md" 2>/dev/null || true

if command -v file &> /dev/null; then
    echo "  executable: $(file -b "$FINAL_DIR/lib/Simitone" | cut -d, -f1-2)"
fi

if [[ "$MAKE_TAR" == "1" ]]; then
    tar -C publish -czf "publish/$PACKAGE.tar.gz" "$PACKAGE"
    echo "  archive: publish/$PACKAGE.tar.gz"
fi

echo ""
echo "========================================"
echo "  Done: $SCRIPT_DIR/$FINAL_DIR"
echo "========================================"
echo ""
echo "  $PACKAGE/"
echo "    Simitone-VR      <- launch this (starts Simitone with -vr)"
echo "    lib/             <- self-contained .NET $RID build"
echo ""
if [[ "$ARCH" == "arm64" ]]; then
    echo "Deploy to the Steam Frame (Developer Mode on): use the SteamOS Devkit Client to upload the folder and set"
    echo "the launch command to 'Simitone-VR', or copy it over SSH and add Simitone-VR as a non-Steam game."
    echo "Details: VR.md"
fi
