#!/bin/bash
# Simitone VR launcher (Steam Frame / SteamOS arm64, also works for linux-x64 + SteamVR).
#
#   ./Simitone-VR                       auto-detects The Sims (Steam library, ../The Sims, Wine)
#   SIMITONE_SIMS_PATH=/path ./Simitone-VR
#   ./Simitone-VR -path"/run/media/.../The Sims/"
#
# Extra arguments are passed through to Simitone.

HERE="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"

# SteamVR on the Steam Frame accepts OpenGL sessions through XR_KHR_opengl_enable on GLX (Xlib), which is
# what SDL gives us under X11/XWayland. (If SDL ends up on EGL, Simitone uses XR_MNDX_egl_enable instead.)
export SDL_VIDEODRIVER="${SDL_VIDEODRIVER:-x11}"

ARGS=(-vr)
if [ -n "$SIMITONE_SIMS_PATH" ]; then
    ARGS+=("-path${SIMITONE_SIMS_PATH%/}/")
fi

# MonoGame needs SDL2 and OpenAL. SteamOS ships both; bundled copies in lib/ take precedence.
for lib in libSDL2-2.0.so.0 libopenal.so; do
    if [ ! -e "$HERE/lib/$lib" ] && ! /sbin/ldconfig -p 2>/dev/null | grep -q " $lib " && \
       ! ldconfig -p 2>/dev/null | grep -q " $lib "; then
        echo "Simitone-VR: warning: $lib not found on this system; Simitone may fail to start." >&2
    fi
done

cd "$HERE/lib" || exit 1
exec ./Simitone "${ARGS[@]}" "$@"
