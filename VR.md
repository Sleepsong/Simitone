# Simitone VR (OpenXR)

> **Status: experimental.** Native VR builds and its OpenXR layer passes headless tests against the Monado
> runtime, but it has **not yet been run on a real headset** (including the Steam Frame) or with a real lot
> loaded. Expect rough edges; see [Known limitations](#known-limitations-and-next-steps).

Simitone can be played in VR through OpenXR. **In a lot you are your Sim**: you see through their eyes, walk
them around with the thumbstick, and point at things to use them. The clock is on your right wrist and your
needs are on your left. Everything that isn't the 3D lot (loading screens, the neighbourhood, CAS, build and
buy mode) appears on a flat virtual screen floating in front of you.

It runs on any OpenXR runtime that supports OpenGL: SteamVR on Windows and Linux, Monado, and natively on the
**Valve Steam Frame** (SteamOS, arm64), standalone or streamed from a PC.

## How it plays

**Play as your Sim (default).** When a lot loads, you're put in your selected Sim's head.
- **Walking:** the left stick walks them relative to where you look, and they run near full tilt (or while you
  hold the stick down). Walls, doors and stairs are the game's own, because this is FreeSO's direct control: the
  same mechanism the gamepad support uses.
- **Turning:** turn with the right stick. Your Sim turns to face where you look.
- **Your own head:** you can lean a little away from the Sim's head, and their head is hidden so you don't see the
  inside of it.
- **Seated Sims:** if your Sim is sitting, holding the stick makes them stand up and step that way.
- **Interacting:** point with the right controller (the laser turns green on something usable) and pull the trigger.
  That object's (or Sim's) real pie menu floats in front of you; point and pull the trigger again to choose.
  Pointing at the floor gives "Go Here". The Sim walks over and does it, then hands control back to you.
- **Dialogs** (phone calls, job offers, carpool...) appear on the same floating panel.
- **Free will** needs no special handling. Direct control re-queues itself every tick, so the Sim you're playing
  always has something queued and free will (which only runs when a Sim's queue is empty) leaves them alone. The
  gamepad work established this approach and confirmed it on hardware.

**Observer** (B to switch). You leave your Sim and roam freely as yourself:
- smooth locomotion, snap or smooth turning, walls that stop you, and right stick up/down to change floors;
- the same pointing and pie menus as in Sim mode, so you can direct Sims like in the normal game.

**Wrist displays.** Turn your wrist to look at it:
- **right wrist:** a watch with the time, game speed and household money;
- **left wrist:** the selected Sim's name and needs.

**Flat screen.** A (or Tab) switches between first person and the normal game on a floating screen, where the
laser pointer is your mouse. Build, buy and options mode, and the travel map, switch to the flat screen
automatically and come back to first person when you're done.

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

## Controls

| Input | First person: Sim | First person: observer | Flat screen |
|---|---|---|---|
| Left stick | Walk your Sim | Move | n/a |
| Left stick click (hold) | Run | Sprint | n/a |
| Right stick left/right | Snap / smooth turn | Snap / smooth turn | n/a |
| Right stick up/down | n/a | Go up / down a floor | Scroll |
| Right stick click | Cycle game speed | Cycle game speed | Cycle game speed |
| Right trigger | Point and interact; click on menus | Point and interact; click on menus | Left click |
| Right grip | Close a pie menu; right click on menus | Close a pie menu; right click on menus | Right click |
| A | Flat screen | Flat screen | First person |
| B | Switch to observer | Switch to playing as your Sim | Recentre the screen |
| X | Pause / resume | Pause / resume | Pause / resume |
| Y | Next Sim in the family | Next Sim in the family | Next Sim in the family |

Bindings are suggested for the **Steam Frame controllers** (`XR_VALVE_frame_controller_interaction`),
**Oculus Touch**, **Valve Index** (left A/B act as X/Y), **HTC Vive** wands (trackpads move and turn) and the
KHR simple controller. SteamVR lets you rebind everything in its controller binding UI.

Keyboard fallback in first person, for testing without controllers: WASD/arrows to move, Shift to sprint,
Q/E to turn, PageUp/PageDown to change floors (observer).

## Settings (`vr.ini`)

`vr.ini` is written with every option and its default on first run, in the Simitone user folder (on Linux
`~/Documents/Simitone/`, or `~/Simitone/` if there is no Documents folder; on Windows `Documents\Simitone\`). A
`vr.defaults.ini` next to the executable, if present, is applied first; the Steam Frame package ships one.

| Key | Default | Meaning |
|---|---|---|
| `PlayMode` | Sim | `Sim` (first person as your Sim) or `Observer` (roam freely) when a lot loads |
| `RenderScale` | 1.0 (Frame: 0.75) | Multiplier on the headset's recommended per-eye resolution |
| `WorldScale` | 3.0 | FreeSO world units per metre. 3 = life size (a TS1 tile is about 1 m) |
| `MoveSpeed` | 1.6 | Observer walking speed, m/s |
| `SprintMultiplier` | 2.5 | Observer speed multiplier while sprinting |
| `MoveDirection` | Head | `Head` or `LeftHand`: what "forward" on the stick means |
| `TurnMode` | Snap | `Snap` or `Smooth` |
| `SnapTurnDegrees` | 30 | Snap turn angle |
| `SmoothTurnDegreesPerSecond` | 120 | Smooth turn speed |
| `WallCollision` | True | Observer: walls stop thumbstick movement (doors don't) |
| `AutoEnterImmersive` | True | Go straight to first person when a lot loads |
| `HeightOffset` | 0 | Observer: metres added to your tracked height (e.g. 0.5 to play seated) |
| `FallbackEyeHeight` | 1.65 | Eye height if the runtime has no floor-level tracking space |
| `ScreenDistance` / `ScreenWidth` | 2.0 / 2.4 | Flat screen distance and width, metres |
| `PanelDistance` / `PanelWidth` | 1.3 / 1.7 | Floating pie menu / dialog panel distance and width, metres |
| `WristDisplays` | Auto | `Auto` (when you look at your wrist), `Always` or `Off` |
| `SimHeadSmoothing` | 8 | How quickly the view follows your Sim's head (per second). Lower is smoother but laggier |
| `LeanLimit` | 0.3 | How far (m) you can lean away from your Sim's head before the view drags along |
| `MirrorEye` | Left (Frame: None) | Eye shown in the desktop window: `Left`, `Right`, `None` |
| `NearClip` | 0.05 | Near clip plane, metres |
| `DeadZone` | 0.2 | Thumbstick dead zone |
| `UpdateRate` | 90 | Game update rate in VR (Hz). The headset paces rendering; a `-hzNN` flag overrides this |

A log of the VR startup (runtime, extensions, chosen formats and spaces, errors) and of mode changes is
written to `vr.log` in the same folder.

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
| The view bobs or lags when playing as your Sim | Adjust `SimHeadSmoothing` (lower = smoother) |
| Wrist displays in the wrong place | They sit on the controller's grip pose, which varies by controller; set `WristDisplays=Always` to see them |
| Game runs too fast / slow in VR | Leave `UpdateRate` at the default or pass `-hzNN`; it must not be changed mid-lot |

## How it works (for developers)

```
Client/Simitone/Simitone.VR/          OpenXR layer, no FreeSO dependency
  OpenXR/XrTypes.cs, XrApi.cs         hand-written OpenXR 1.0 bindings (function pointers, no NuGet deps)
  OpenXR/XrLoader.cs                  Khronos loader, or direct runtime negotiation via active_runtime.json
  Graphics/GLInterop.cs               MonoGame/SDL GL context -> GLX/EGL/WGL handles; FBO blits into swapchains
  VRRuntime.cs                        instance/system/session/spaces/swapchains/frame loop/layers/events
  VRInput.cs                          action set + suggested bindings (Frame, Touch, Index, Vive, simple)
  VRSettings.cs, VRMath.cs
  SteamFrame/                         launcher script and vr.defaults.ini for the arm64 package
Client/Simitone/Simitone.Client/VR/
  SimitoneVR.cs                       game-loop glue: frame begin/end, flat screen, UI panel, laser mouse, buttons
  VRLotWorld.cs                       World subclass: renders the lot once per eye + pointer beam; FreeSO picking
  VRCameraController.cs               eye matrices; Sim mode (head anchor, lean) and observer locomotion
  VRGameBridge.cs                     direct control of your Sim, pointing -> pie menus, flat screen for build/buy
  VRUILayer.cs                        captures the game UI into its own texture for the floating panel
  VRWristHud.cs, VRPixelFont.cs       watch and needs displays
```

Per frame, all on the game thread inside MonoGame's `Draw`:

1. `SimitoneVR.BeginDraw`:
   - `xrWaitFrame` (this paces the game to the headset), `xrBeginFrame`, `xrLocateViews`, `xrSyncActions`;
   - controller buttons become requests for the next update;
   - the wrist displays are redrawn.
2. FreeSO draws as usual.
   - The VR camera's `PreDraw` anchors the view to your Sim's head (or moves the observer) and builds the eye
     matrices.
   - `VRLotWorld.InternalDraw` runs FreeSO's 3D draw once per eye into a MonoGame render target, with the
     pointer beam.
   - The UI is drawn into its own texture (`VRUILayer`) and onto the window.
3. `SimitoneVR.EndDraw`:
   - each eye is blitted into an OpenXR swapchain image, flipped because MonoGame stores render targets
     top-down;
   - the eyes go out as a projection layer, with the wrist displays and (if a pie menu or dialog is up) the UI
     panel as alpha-blended quad layers;
   - outside first person, the window goes out as one flat quad;
   - then `xrEndFrame`.

On the next game update, `VRGameBridge`:
- turns direct control on or off for the selected Sim (`VMNetDirectControlToggleCommand`) and feeds the stick
  to its `VMDirectControlFrame`;
- picks what the controller points at (FreeSO's own picker, run through a camera looking down the ray);
- opens pie menus with `UILotControl.OpenPieMenu`.

Design choices:

- **FreeSO is used as-is**, at the gamepad work's engine (`Sleepsong/FreeSO` `simitone/controller-phase7`,
  `e48e84de`) for TS1 direct control. Everything VR hooks in through subclasses (`World`, `CameraControllerFP`,
  `WorldCamera3D`), public fields and a layer wrapper. `UILotControl.OpenPieMenu` has the same name and
  signature as on the controller branch.
- **Fixed-timestep updates at `UpdateRate`.** Vsync is turned off and `xrWaitFrame` paces drawing; MonoGame
  catches up on updates when frames take longer, so simulation speed doesn't depend on the headset's refresh
  rate. A running session also counts as window focus, so a headset user never sees MonoGame's inactive
  slowdown or a deaf UI.
- **sRGB swapchains with a raw copy.** MonoGame renders gamma-space colours into RGBA8 targets, and copying
  those bits unconverted into an `SRGB8_ALPHA8` swapchain gives the correct result.

### Tests

- `tools/vr/layout-check/run.sh` compares every hand-written OpenXR struct's size and field offsets with the
  Khronos C headers (165 checks).
- `tools/vr/smoke-test/run-monado.sh` opens a MonoGame DesktopGL window under Xvfb, starts a session on the
  Monado runtime (simulated HMD, null compositor), renders both eyes plus opaque and alpha-blended quads, and
  checks pixel orientation and GL errors. Variables: `SIMITONE_XR_LOADER=direct` exercises the loader-free
  path, `SDL_VIDEO_X11_FORCE_EGL=1` the EGL binding. Ubuntu packages: `monado-service libopenxr1-monado
  libopenxr-loader1 xvfb mesa-vulkan-drivers libvulkan1 libgl1-mesa-dri libsdl2-2.0-0`.

## Known limitations and next steps

- **Untested on real hardware**, including the Steam Frame, and untested with a real lot (needs The Sims). That
  includes Sim mode, pointing, the floating panel and the wrist displays.
- **Uncertain hardware details:**
  - The Frame controller's face-button paths are inferred from its Touch-like layout. If SteamVR rejects them,
    it falls back to a reduced binding set and then to the Touch bindings (logged in `vr.log`).
  - The wrist display offsets are estimates and may need tuning per controller.
- **Riding along.** While your Sim walks to and carries out a queued action, the view goes with them; you can
  look around but not steer until they finish, which can be uncomfortable for some people.
- **Pie menus** float in front of you rather than at the object. They are the regular 2D menus.
- **Observer mode:** stairs aren't walked up (use right stick up/down at the stairs), and objects don't
  collide.
- **Performance:**
  - the lot is drawn twice (no multiview);
  - no foveated rendering (`XR_FB_foveation` / eye-tracked foveation on the Frame);
  - the Steam Frame defaults are a guess.
- Windows VR is implemented (WGL binding; loader or registry runtime lookup) but untested.
- Cosmetic: FreeSO smooths avatar normals in its draw call, so with two eye passes that smoothing runs twice as
  fast.

**Planned: VR build and buy mode.** Today build and buy are on the flat screen. The plan is for you to stand
in one spot in Sim view and place objects remotely: a beam from the controller picks up or aims a catalogue
object, the placement preview follows where you point (snapping to tiles and rotating with the stick), and the
trigger places it. Think Half-Life: Alyx's gravity gloves, or the arc of VR teleport locomotion, but placing
furniture instead of moving yourself. The pointer, FreeSO picking and floor-hit code in `VRGameBridge` are the
starting point, along with `UILotControl`'s object holder.
