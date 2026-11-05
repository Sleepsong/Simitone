# Simitone VR (OpenXR)

> **Status: experimental, first milestone.** This is the start of native VR support. It builds and its
> OpenXR layer passes headless tests against the Monado runtime, but it has **not yet been run on a real
> headset** (including the Steam Frame) or with a real lot loaded. Expect rough edges; see
> [Known limitations](#known-limitations-and-next-steps).

Simitone's 3D mode can be played in VR through OpenXR. You walk around your lot in first person at life
size, with full smooth locomotion, while the simulation keeps running around you. Menus and anything else
that isn't the lot (CAS, the neighbourhood, build/buy, dialogs) appear on a floating virtual screen that you
point at with the controller.

It runs on any OpenXR runtime that supports OpenGL: SteamVR on Windows and Linux, Monado, and natively on the
**Valve Steam Frame** (SteamOS, arm64), standalone or streamed from a PC.

## What works in this milestone

- **Immersive first-person view** of the lot in 3D, rendered per eye with full 6DoF head tracking.
  It is FreeSO's normal 3D renderer drawn once per eye (lighting, shadows, avatars, weather).
- **Full locomotion**: smooth thumbstick movement (head- or controller-relative), sprint, snap or smooth
  turning around your head, and real room-scale movement.
- **Walls block you, doors don't.** Artificial movement slides along walls; your physical movement is never
  blocked.
- **Floors**: walking off an upper floor drops you to the one below. Right stick up/down climbs or descends a
  floor, and upper floors and roofs are drawn above you as ceilings.
- **Jump to the selected Sim** (B), and on lot load you start next to them.
- **Virtual screen** for everything else, with a laser-pointer mouse (trigger = click, grip = right click,
  right stick = scroll). When a modal dialog pops up in-game, you're moved to the screen automatically and put
  back where you were once it's answered.
- Pause (X) and game speed (Y) from the controllers.
- Desktop mirror of one eye. Settings live in `vr.ini`.

## Running on a PC

1. Install an OpenXR runtime and make it the active one (SteamVR: *Settings → OpenXR → Set SteamVR as
   OpenXR runtime*).
2. Install the [3D mesh pack](http://forum.freeso.org/forums/3d-remeshing.40/) as for normal 3D mode
   (optional, but the generated meshes look rougher).
3. Start Simitone with `-vr` (it implies `-3d`):

   ```
   ./Simitone -vr
   ./Simitone -vr -path"/path/to/The Sims/"
   ```

VR needs the cross-platform **OpenGL** build (`Simitone.Desktop`, which `build-mac-linux.sh` and
`build-steamframe.sh --x64` produce). The DirectX-only `Simitone.Windows` build does not support VR.
If no runtime or headset is found, Simitone logs why and carries on as the normal desktop game.

When a lot finishes loading you're dropped into first person next to the selected Sim. **A** (or **Tab** on the
keyboard) switches between first person and the normal camera on the virtual screen.

## Controls

| Input | Immersive (first person) | Virtual screen |
|---|---|---|
| Left stick | Move | n/a |
| Left stick click (hold) | Sprint | n/a |
| Right stick left/right | Snap / smooth turn | n/a |
| Right stick up/down | Go up / down a floor | Scroll |
| Right trigger | n/a | Left click |
| Right grip | n/a | Right click |
| A | Switch to the virtual screen | Switch to first person |
| B | Jump to the selected Sim | Recentre the screen in front of you |
| X | Pause / resume | Pause / resume |
| Y | Cycle game speed (1-2-3) | Cycle game speed |

Bindings are suggested for the **Steam Frame controllers** (`XR_VALVE_frame_controller_interaction`),
**Oculus Touch**, **Valve Index**, **HTC Vive** wands (trackpads move and turn) and the KHR simple controller.
SteamVR lets you rebind everything in its controller binding UI.

Keyboard fallback in first person, for testing without controllers: WASD/arrows to move, Shift to sprint,
Q/E to turn, PageUp/PageDown to change floors.

## Settings (`vr.ini`)

`vr.ini` is written with every option and its default on first run, in the Simitone user folder
(on Linux `~/Documents/Simitone/`, or `~/Simitone/` if there is no Documents folder; on Windows `Documents\Simitone\`). A
`vr.defaults.ini` next to the executable, if present, is applied first; the Steam Frame package ships one.

| Key | Default | Meaning |
|---|---|---|
| `RenderScale` | 1.0 (Frame: 0.75) | Multiplier on the headset's recommended per-eye resolution |
| `WorldScale` | 3.0 | FreeSO world units per metre. 3 = life size (a TS1 tile is about 1 m) |
| `MoveSpeed` | 1.6 | Walking speed, m/s |
| `SprintMultiplier` | 2.5 | Speed multiplier while sprinting |
| `MoveDirection` | Head | `Head` or `LeftHand`: what "forward" on the stick means |
| `TurnMode` | Snap | `Snap` or `Smooth` |
| `SnapTurnDegrees` | 30 | Snap turn angle |
| `SmoothTurnDegreesPerSecond` | 120 | Smooth turn speed |
| `WallCollision` | True | Walls stop thumbstick movement (doors don't) |
| `AutoEnterImmersive` | True | Go straight to first person when a lot loads |
| `HeightOffset` | 0 | Metres added to your tracked height (e.g. 0.5 to play seated) |
| `FallbackEyeHeight` | 1.65 | Eye height if the runtime has no floor-level tracking space |
| `ScreenDistance` / `ScreenWidth` | 2.0 / 2.4 | Virtual screen distance and width, metres |
| `MirrorEye` | Left (Frame: None) | Eye shown in the desktop window: `Left`, `Right`, `None` |
| `NearClip` | 0.05 | Near clip plane, metres |
| `DeadZone` | 0.2 | Thumbstick dead zone |
| `UpdateRate` | 90 | Game update rate in VR (Hz). The headset paces rendering; a `-hzNN` flag overrides this |

A log of the VR startup (runtime, extensions, chosen formats and spaces, errors) is written to `vr.log` in
the same folder.

## Steam Frame

The Steam Frame is an arm64 SteamOS headset. There are two ways to play:

### A. Standalone (native arm64, on the headset)

x86 builds running under FEX cannot load the headset's native arm64 OpenXR runtime, so VR on the Frame
itself needs a native **linux-arm64** build. `build-steamframe.sh` produces one from any Linux (or WSL)
machine with the .NET 9 SDK or newer. No arm64 compilers are needed: only managed code is built, and the
arm64 .NET runtime comes from NuGet.

```
./build-steamframe.sh            # -> publish/Simitone-SteamFrame-arm64/
./build-steamframe.sh --tar      # also publish/Simitone-SteamFrame-arm64.tar.gz
```

The package is self-contained:

```
Simitone-SteamFrame-arm64/
  Simitone-VR        <- launcher: sets SDL_VIDEODRIVER=x11 and runs Simitone with -vr
  lib/               <- self-contained .NET linux-arm64 build + vr.defaults.ini
```

What it relies on from SteamOS:

- **SteamVR's arm64 OpenXR runtime.** Simitone reads `active_runtime.json` and talks to the runtime directly,
  so no `libopenxr_loader` is needed. If the Khronos loader is installed or bundled, it is used instead.
- **OpenGL through Mesa (Zink on Turnip).** Simitone hands SteamVR its SDL/GLX context through
  `XR_KHR_opengl_enable` with the Xlib binding, the route other native OpenGL ports on the Frame use. If SDL
  ends up on EGL instead, `XR_MNDX_egl_enable` is used.
- **SDL2 and OpenAL** system libraries (MonoGame doesn't ship arm64 builds). SteamOS includes both. To
  bundle your own, pass `--native-dir DIR` containing arm64 `libSDL2-2.0.so.0` / `libopenal.so`.

Deploying:

1. Enable **Developer Mode** on the headset.
2. Either use Valve's **SteamOS Devkit Client** to upload `publish/Simitone-SteamFrame-arm64/` with
   `Simitone-VR` as the launch command, **or** copy the folder over SSH (e.g. to `~/Simitone-VR/`) and add
   `Simitone-VR` as a non-Steam game from Desktop Mode.
3. Make The Sims available on the headset. If the Steam version (*The Sims Legacy Collection*) is installed
   in the internal Steam library (`~/.steam/steam/steamapps/common/The Sims/`), it is found automatically.
   Otherwise set `SIMITONE_SIMS_PATH=/path/to/The Sims` in the launch options, or pass `-path"..."`.

Performance: the Frame renders around 1440-1728 px per eye and FreeSO draws the lot twice per frame.
`RenderScale=0.75` is the starting point; lower it if SteamVR is reprojecting heavily, raise it if your lot
holds frame rate.

### B. Streaming from a PC

The Frame is designed to stream PC VR. Run the normal desktop build with `-vr` on a Windows or Linux PC
with SteamVR, and connect the Frame to SteamVR on that PC (Steam Link). Nothing Frame-specific is needed.
`./build-steamframe.sh --x64` builds a Linux x64 package with the same launcher.

## Troubleshooting

| Symptom | Try |
|---|---|
| `no active OpenXR runtime manifest` | Set your runtime as the active OpenXR runtime, or point `XR_RUNTIME_JSON` at its manifest |
| `xrGetSystem failed` | Headset not connected, or SteamVR not running |
| `SDL created an EGL context but the runtime lacks XR_MNDX_egl_enable` | Run with `SDL_VIDEODRIVER=x11` |
| Wrong runtime / loader picked | `SIMITONE_XR_LOADER=khronos` (loader only) or `SIMITONE_XR_LOADER=direct` (bypass the loader) |
| Low frame rate | Lower `RenderScale`; set `MirrorEye=None` |
| Everything is giant / tiny | Adjust `WorldScale` |
| Game runs too fast / slow in VR | Leave `UpdateRate` at the default or pass `-hzNN`; it must not be changed mid-lot |

## How it works (for developers)

```
Client/Simitone/Simitone.VR/          OpenXR layer, no FreeSO dependency
  OpenXR/XrTypes.cs, XrApi.cs         hand-written OpenXR 1.0 bindings (function pointers, no NuGet deps)
  OpenXR/XrLoader.cs                  Khronos loader, or direct runtime negotiation via active_runtime.json
  Graphics/GLInterop.cs               MonoGame/SDL GL context -> GLX/EGL/WGL handles; FBO blits into swapchains
  VRRuntime.cs                        instance/system/session/spaces/swapchains/frame loop/events
  VRInput.cs                          action set + suggested bindings (Frame, Touch, Index, Vive, simple)
  VRSettings.cs, VRMath.cs
  SteamFrame/                         launcher script and vr.defaults.ini for the arm64 package
Client/Simitone/Simitone.Client/VR/
  SimitoneVR.cs                       game-loop glue: BeginDraw/EndDraw, virtual screen, laser pointer, dialogs
  VRLotWorld.cs                       World subclass: renders the lot once per eye, mirrors to the window
  VRCameraController.cs               eye matrices, locomotion, wall collision, floors (FreeSO's FP camera slot)
```

Per frame, all on the game thread inside MonoGame's `Draw`:

1. `SimitoneVR.BeginDraw`: `xrWaitFrame` (this paces the game to the headset), `xrBeginFrame`,
   `xrLocateViews`, `xrSyncActions`.
2. FreeSO draws as usual. If the VR camera is active, `VRCameraController.PreDraw` applies locomotion and
   builds the eye matrices, then `VRLotWorld.InternalDraw` runs FreeSO's 3D draw once per eye into a
   MonoGame render target.
3. `SimitoneVR.EndDraw`: each eye is blitted (flipped: MonoGame stores render targets top-down) into an
   OpenXR swapchain image and submitted as a projection layer. When not immersive, the back buffer is
   blitted into a quad layer (the virtual screen) instead. Then `xrEndFrame`.

Design choices:

- **No FreeSO changes.** Everything hooks in through subclasses (`World`, `CameraControllerFP`,
  `WorldCamera3D`) and public fields, so the FreeSO submodule stays untouched.
- **Fixed-timestep updates at `UpdateRate`.** Vsync is turned off and `xrWaitFrame` paces drawing; MonoGame
  catches up on updates when frames take longer, so simulation speed doesn't depend on the headset's refresh
  rate.
- **sRGB swapchains with a raw copy.** MonoGame renders gamma-space colours into RGBA8 targets, and copying
  those bits unconverted into an `SRGB8_ALPHA8` swapchain gives the correct result.

### Tests

- `tools/vr/layout-check/run.sh` compares every hand-written OpenXR struct's size and field offsets with the
  Khronos C headers (165 checks).
- `tools/vr/smoke-test/run-monado.sh` opens a MonoGame DesktopGL window under Xvfb, starts a session on the
  Monado runtime (simulated HMD, null compositor), renders both eyes and a virtual screen, and checks pixel
  orientation and GL errors. Variables: `SIMITONE_XR_LOADER=direct` exercises the loader-free path,
  `SDL_VIDEO_X11_FORCE_EGL=1` the EGL binding. Ubuntu packages: `monado-service libopenxr1-monado
  libopenxr-loader1 xvfb mesa-vulkan-drivers libvulkan1 libgl1-mesa-dri libsdl2-2.0-0`.

## Known limitations and next steps

- **Untested on real hardware**, including the Steam Frame, and untested with a real lot (needs The Sims).
  The Frame controller face-button paths are inferred from Valve's Touch-like layout; if SteamVR rejects
  them, it falls back to a reduced binding set and then to the Touch bindings (logged in `vr.log`).
- **No in-world interaction yet.** You can't point at objects or Sims to open the pie menu in first person.
  Use the virtual screen (A). This is the most important next step: a controller ray into
  `GetObjectIDAtScreenPos` plus a world-space pie menu.
- **You are an observer, not a Sim.** The VR camera roams freely and doesn't control anyone, so free will
  needs no special handling. A "play as your Sim" mode (camera on the Sim's head, the stick walking them)
  would build on the gamepad work's TS1 direct control (`VM.AllowDirectControl`, `VMDirectControlFrame.TS1Routine`
  on Sleepsong/FreeSO `simitone/controller-phase4`+, driven from `UILotControlPadHelper` on the Simitone
  controller branch). That branch doesn't switch free will off for the controlled Sim. It relies on the
  direct-control action being re-queued every tick, so the Sim's queue is never empty and TS1's idle-loop free
  will never fires. That was confirmed on hardware ("the Sim stays put"). This branch is based on
  `simitone-forked`, so it would also need the FreeSO submodule moved to that branch (it already merges the
  current pin, `d8a3c175`).
- **Stairs** aren't walked up; use right stick up/down at the stairs. Objects don't collide.
- **The game UI isn't shown in first person**, apart from the automatic switch for modal dialogs. A HUD
  (needs, time, money) as a quad layer would be next.
- **Performance**: the lot is drawn twice (no multiview); no foveated rendering (`XR_FB_foveation` /
  eye-tracked foveation on the Frame); Steam Frame defaults are a guess.
- Windows VR is implemented (WGL binding; loader or registry runtime lookup) but untested.
- The desktop window shows the mirror while immersive; the 2D/desktop UI is hidden as in FreeSO's
  first-person mode.
