#!/bin/bash
# Headless end-to-end test of Simitone.VR against the Monado OpenXR runtime (no headset needed).
#
# Ubuntu/Debian packages: monado-service libopenxr1-monado libopenxr-loader1 xvfb mesa-vulkan-drivers
#                         libvulkan1 libgl1-mesa-dri libsdl2-2.0-0
# Usage: tools/vr/smoke-test/run-monado.sh [frames]      (SIMITONE_XR_LOADER=direct to skip the Khronos loader,
#                                                          SDL_VIDEO_X11_FORCE_EGL=1 to test the EGL binding)
set -e
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="$(mktemp -d)"
export XDG_RUNTIME_DIR="$WORK/xdg"; mkdir -p "$XDG_RUNTIME_DIR"; chmod 700 "$XDG_RUNTIME_DIR"
export XR_RUNTIME_JSON="${XR_RUNTIME_JSON:-/usr/share/openxr/1/openxr_monado.json}"
export DISPLAY=:97

cleanup() { kill $XVFB_PID $SLEEP_PID 2>/dev/null || true; pkill -f "monado-service" 2>/dev/null || true; }
trap cleanup EXIT

Xvfb :97 -screen 0 1280x720x24 > "$WORK/xvfb.log" 2>&1 & XVFB_PID=$!
# Monado's null compositor still needs Vulkan (lavapipe is fine). monado-service also insists on an
# epoll-able stdin, so feed it a pipe.
sleep 100000 | VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json XRT_COMPOSITOR_NULL=1 monado-service > "$WORK/monado.log" 2>&1 &
SLEEP_PID=$(pgrep -n -f "sleep 100000")
for i in $(seq 1 50); do [ -S "$XDG_RUNTIME_DIR/monado_comp_ipc" ] && break; sleep 0.2; done

dotnet build "$DIR/SmokeTest.csproj" -c Release -o "$WORK/bin" -v q -nologo > "$WORK/build.log" || { cat "$WORK/build.log"; exit 1; }
dotnet "$WORK/bin/SmokeTest.dll" "${1:-240}"
