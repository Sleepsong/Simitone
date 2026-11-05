# Simitone VR (OpenXR)

> **Status: experimental.** Native VR builds and its OpenXR layer passes headless tests against the Monado
> runtime, but it has **not yet been run on a real headset** (including the Steam Frame) or with a real lot
> loaded. Expect rough edges; see [Known limitations](#known-limitations-and-next-steps).

Simitone can be played in VR through OpenXR. **In a lot you are your Sim**: you see through their eyes, walk
them around with the thumbstick, and point at things to use them. The clock is on your right wrist and your
needs are on your left.

**Comfort comes first.** When your Sim acts on their own (walking to the fridge, sitting down, going to bed), your
view never slides or bobs about by default: you step out, watch, and follow with quick blinks. Three comfort
levels, a vignette, stick or arc movement and the other options can all be changed in the headset, from the
**VR menu**.

In **buy and build mode** you stay where you are and place things remotely. A beam picks objects up from a
distance, and a teleport-style arc shows where they'll land.

Everything that isn't the 3D lot (loading screens, the neighbourhood, CAS, options) appears on a flat virtual
screen floating in front of you.

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
- **Movement arc:** hold the left trigger, aim the arc at the floor and let go: your Sim walks there (it's the
  floor's "Go Here"). Cyan means you can go there, red means you can't.
- **Seated Sims:** if your Sim is sitting, holding the stick makes them stand up and step that way.
- **Interacting:** point with the right controller (the laser turns green on something usable) and pull the trigger.
  That object's (or Sim's) real pie menu floats in front of you; point and pull the trigger again to choose.
  Pointing at the floor gives "Go Here". The Sim walks over and does it, then hands control back to you. What
  your view does meanwhile is up to the comfort setting (below).
- **Dialogs** (phone calls, job offers, carpool...) appear on the same floating panel.
- **Free will** needs no special handling. Direct control re-queues itself every tick, so the Sim you're playing
  always has something queued and free will (which only runs when a Sim's queue is empty) leaves them alone. The
  gamepad work established this approach and confirmed it on hardware.

**Comfort while your Sim acts on their own** (`SimActionView`, in the VR menu as *Sim actions*):
- **Comfortable** (default): when your Sim walks off to do something, you blink out of their head (a quick fade
  to black and back) and stand just behind them. You stay put and watch; when they get more than 4 m away, leave
  the room or change floor, you blink to a spot near them. When you can steer again, or they settle (sat down,
  busy in one place), you blink back into their head.
- **Moderate:** the same step out, but then you glide after them at a steady walking pace, in third person. You
  never turn by yourself, and you blink instead when they go round a corner or through a door.
- **Intense:** you ride along in their head the whole time, with a vignette while you're carried.
- **All levels:**
  - the view never turns by itself;
  - your Sim's walk cycle doesn't bob you about (you follow their position, not their head bone);
  - sitting down, standing up and going up or down stairs are a blink to the new height, not a ride;
  - when your Sim lies down (bed, bath, sofa nap) you step out and watch rather than lie down with them.

**Vignette.** The edge of your view darkens while it moves without you moving your head. This is the
best-supported single fix for motion sickness.
- `Vignette` sets the strength while your Sim carries you or you glide after them (Medium by default).
- `VignetteWhenMoving` sets it while you walk with the stick or turn smoothly (Low by default).

**VR menu.** Open it with the left controller's menu button (Touch-style controllers). On Index, squeeze the left
trackpad. On any controller, point at your left wrist (the needs display lights up) and pull the trigger.
On the desktop keyboard, press F9.
- **Using it:** point at an option; the trigger steps it forward and the grip steps it back.
- **Options:** Sim actions comfort, vignette strengths, movement (stick, arc or both), turning (snap 30/45/90 or
  smooth), walk direction (head or left hand), play as Sim or observer, extra height (for playing seated), wrist
  displays, buy/build in VR or on the flat screen, and render scale.
- **Saving:** changes apply immediately and are saved to `vr.ini`.
- **Closing:** pull the trigger beside the menu, choose *Close*, or press the menu button again.

**Observer** (B to switch). You leave your Sim and roam freely as yourself:
- smooth locomotion, snap or smooth turning, walls that stop you, and right stick up/down to change floors;
- **teleport:** hold the left trigger, aim the arc and let go to blink there;
- the same pointing and pie menus as in Sim mode, so you can direct Sims like in the normal game.

**Wrist displays.** Turn your wrist to look at it:
- **right wrist:** a watch with the time, game speed and household money;
- **left wrist:** the selected Sim's name and needs.

**Buy and build mode** (left grip cycles live → buy → build → live, or pick the mode on the flat screen). You
stay in first person, where you were standing. The game pauses, as usual, and you build on the floor you're on.
- **The catalogue** is on a desk-like panel below you; point at it and pull the trigger to choose something.
- **Placing:** what you're holding hangs at the end of an arc from your controller, like a teleport arc. Green
  means it fits, red means it doesn't. Aim and pull the trigger to place it.
  - Right stick up/down throws the arc further or nearer.
  - Right stick left/right turns the object 90 degrees.
- **Moving existing objects:** point the straight beam at an object (green when you're on one) and pull the
  trigger to pick it up, gravity-gun style; it stays where it is until you aim the arc somewhere else.
- **Cancel and sell:** the right grip puts it back or cancels (as Escape does), and B sells or deletes what you're
  holding.
- **Build tools** work the same way: walls, floors, wallpaper, terrain, doors and windows. Pick the tool on the
  panel, then aim the arc at the floor; walls and floors are dragged by holding the trigger while you sweep the
  arc.
- **Other floors:** in observer mode (B) right stick up/down changes the floor you're standing and building on.

**Flat screen.** A (or Tab) switches between first person and the normal game on a floating screen, where the
laser pointer is your mouse. Options mode and the travel map switch to the flat screen automatically and come
back to first person when you're done. With `VRBuildMode=False`, buy and build use the flat screen too.

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

| Input | First person: Sim | First person: observer | Buy / build (first person) | Flat screen |
|---|---|---|---|---|
| Left stick | Walk your Sim | Move | (observer: move) | n/a |
| Left stick click (hold) | Run | Sprint | (observer: sprint) | n/a |
| Left grip | Buy mode | Buy mode | Next mode (buy → build → live) | Next mode |
| Left trigger (hold, aim, let go) | Movement arc: your Sim walks there | Teleport there | (observer: teleport) | n/a |
| Left menu button (Touch); left trackpad squeeze (Index); trigger on the left wrist | VR menu | VR menu | VR menu | VR menu |
| Right stick left/right | Snap / smooth turn | Snap / smooth turn | Turn what you hold; else turn | n/a |
| Right stick up/down | n/a | Go up / down a floor | Arc reach while holding; else (observer) floor | Scroll |
| Right stick click | Cycle game speed | Cycle game speed | n/a (paused) | Cycle game speed |
| Right trigger | Point and interact; click on menus | Point and interact; click on menus | Pick up / place / drag a tool; choose in the catalogue | Left click |
| Right grip | Close a pie menu; right click on menus | Close a pie menu; right click on menus | Cancel (put back) | Right click |
| A | Flat screen | Flat screen | Flat screen | First person |
| B | Switch to observer | Switch to playing as your Sim | Sell / delete what you hold; else switch Sim / observer | Recentre the screen |
| X | Pause / resume | Pause / resume | n/a | Pause / resume |
| Y | Next Sim in the family | Next Sim in the family | n/a | Next Sim in the family |

Bindings are suggested for the **Steam Frame controllers** (`XR_VALVE_frame_controller_interaction`),
**Oculus Touch**, **Valve Index** (left A/B act as X/Y), **HTC Vive** wands (trackpads move and turn) and the
KHR simple controller. SteamVR lets you rebind everything in its controller binding UI.

Keyboard fallback in first person, for testing without controllers: WASD/arrows to move, Shift to sprint,
Q/E to turn, PageUp/PageDown to change floors (observer). F9 opens the VR menu.

## Settings (`vr.ini`)

`vr.ini` is written with every option and its default on first run, in the Simitone user folder (on Linux
`~/Documents/Simitone/`, or `~/Simitone/` if there is no Documents folder; on Windows `Documents\Simitone\`). A
`vr.defaults.ini` next to the executable, if present, is applied first; the Steam Frame package ships one.

| Key | Default | Meaning |
|---|---|---|
| `PlayMode` | Sim | `Sim` (first person as your Sim) or `Observer` (roam freely) when a lot loads |
| `SimActionView` | Comfortable | What your view does while your Sim acts on their own: `Comfortable` (step out, follow by blinks), `Moderate` (step out, glide after them), `Intense` (ride along) |
| `Vignette` | Medium | Vignette while the view moves without you driving it: `Off`, `Low`, `Medium`, `High` |
| `VignetteWhenMoving` | Low | Vignette while you move with the stick or turn smoothly |
| `Movement` | StickAndArc | `StickAndArc`, `Stick` (left stick only) or `Arc` (left trigger arc only: walk your Sim there, or teleport as an observer) |
| `LeashDistance` | 4 | Comfortable: how far (m) your Sim can get before you blink after them |
| `GlideSpeed` | 1.4 | Moderate: the steady speed (m/s) you glide after your Sim at |
| `BlinkDuration` | 0.25 | Length of a blink (fade out, move, fade in), seconds. 0 makes moves instant |
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
| `HeightOffset` | 0 | Metres added to your tracked height when you're not in your Sim's head (observer, or stepped out), e.g. 0.5 to play seated |
| `FallbackEyeHeight` | 1.65 | Eye height if the runtime has no floor-level tracking space |
| `ScreenDistance` / `ScreenWidth` | 2.0 / 2.4 | Flat screen distance and width, metres |
| `PanelDistance` / `PanelWidth` | 1.3 / 1.7 | Floating pie menu / dialog panel distance and width, metres (the buy/build catalogue desk is closer, lower and 90% as wide) |
| `VRBuildMode` | True | Buy and build in first person with remote placement. `False` uses the flat screen instead |
| `ArcReach` | 7 | Starting launch speed of the placement arc, m/s (the right stick changes it while placing) |
| `WristDisplays` | Auto | `Auto` (when you look at your wrist), `Always` or `Off` |
| `SimHeadSmoothing` | 8 | How quickly the view follows your Sim's position (per second). Lower is smoother but laggier |
| `LeanLimit` | 0.3 | How far (m) you can lean away from your Sim's head before the view drags along |
| `MirrorEye` | Left (Frame: None) | Eye shown in the desktop window: `Left`, `Right`, `None` |
| `NearClip` | 0.05 | Near clip plane, metres |
| `DeadZone` | 0.2 | Thumbstick dead zone |
| `UpdateRate` | 90 | Game update rate in VR (Hz). The headset paces rendering; a `-hzNN` flag overrides this |

Most of these can also be changed in the headset from the [VR menu](#how-it-plays); it saves them back to
`vr.ini`.

A log of the VR startup (runtime, extensions, chosen formats and spaces, errors), mode changes and VR menu changes
is written to `vr.log` in the same folder.

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
| Queasy while your Sim walks you around | Use `SimActionView=Comfortable` and a stronger `Vignette` (VR menu) |
| Too many blinks | `SimActionView=Moderate`, a longer `LeashDistance`, or `BlinkDuration=0` for instant moves |
| No way to open the VR menu on your controller | Point at your left wrist (needs `WristDisplays` not `Off`) and pull the trigger, or press F9 |
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
  VRCameraController.cs               eye matrices; Sim mode (comfort tiers, blinks, lean), observer locomotion,
                                      vignette amount, movement arc
  VRComfortOverlay.cs                 vignette ring and blink fade, drawn last in each eye
  VRMenu.cs                           the VR options menu (quad layer; live apply, saved to vr.ini)
  VRGameBridge.cs                     direct control of your Sim, pointing -> pie menus, buy/build mode, flat screen,
                                      movement arc -> "Go Here"
  VRUILayer.cs                        captures the game UI into its own texture for the floating panel
  VRWristHud.cs, VRPixelFont.cs       watch and needs displays
```

Per frame, all on the game thread inside MonoGame's `Draw`:

1. `SimitoneVR.BeginDraw`:
   - `xrWaitFrame` (this paces the game to the headset), `xrBeginFrame`, `xrLocateViews`, `xrSyncActions`;
   - controller buttons become requests for the next update;
   - the VR menu takes the pointer while it's open;
   - the wrist displays and the menu are redrawn.
2. FreeSO draws as usual.
   - The VR camera's `PreDraw` advances any blink, decides where the view is (in your Sim's head, stepped out,
     gliding, or the observer), traces the movement arc, measures how fast the rig is carrying you (the
     vignette), and builds the eye matrices.
   - `VRLotWorld.InternalDraw` runs FreeSO's 3D draw once per eye into a MonoGame render target, then the
     pointer beam and arcs, then the vignette and blink fade (`VRComfortOverlay`).
   - The UI is drawn into its own texture (`VRUILayer`) and onto the window.
3. `SimitoneVR.EndDraw`:
   - each eye is blitted into an OpenXR swapchain image, flipped because MonoGame stores render targets
     top-down;
   - the eyes go out as a projection layer, with the wrist displays and (if a pie menu or dialog is up) the UI
     panel as alpha-blended quad layers;
   - outside first person, the window goes out as one flat quad;
   - the VR menu, when open, goes on top as one more quad;
   - then `xrEndFrame`.

On the next game update, `VRGameBridge`:
- turns direct control on or off for the selected Sim (`VMNetDirectControlToggleCommand`) and feeds the stick
  to its `VMDirectControlFrame`;
- tells the camera whether you can steer the Sim right now (direct control is on top of their stack) and gives it
  a room lookup, which is all the comfort tiers need;
- sends a "Go Here" (`VMNetGotoCmd`) where the movement arc was let go;
- picks what the controller points at (FreeSO's own picker, run through a camera looking down the ray);
- opens pie menus with `UILotControl.OpenPieMenu`.

**Buy and build mode** reuses everything Simitone already does with the mouse: the object holder
(`UIObjectHolder`), which buys, moves, rotates, places and sells with all the game's placement rules, and the
build tools (`UICustomLotControl`s).
- **Virtual mouse:** off the catalogue, `SimitoneVR` parks the mouse in the middle of the window and drives its
  left button with the trigger.
- **Cursor camera:** `VRCameraController.UpdateBuildCursor` replaces the "mono" camera, which the UI's
  screen-to-world maths uses, with one whose shifted, tiny-FOV projection makes that pixel unproject exactly onto
  your aim:
  - while placing: a camera straight above the arc's landing point;
  - while picking: a camera looking down the controller's ray.
- **Result:** `World.EstTileAtPosWithScroll` and `GetObjectIDAtScreenPos` see the mouse "at" the arc or beam.
- **On the catalogue:** because the field of view is tiny, a mouse sitting there still maps to practically the
  same spot, so a held object doesn't fly about while you choose.

Design choices:

- **FreeSO is used as-is**, at the gamepad work's engine (`Sleepsong/FreeSO` `simitone/controller-phase7`,
  `e48e84de`) for TS1 direct control. Everything VR hooks in through subclasses (`World`, `CameraControllerFP`,
  `WorldCamera3D`), public fields and a layer wrapper. `UILotControl.OpenPieMenu` has the same name and
  signature as on the controller branch.
- **Fixed-timestep updates at `UpdateRate`.** Vsync is turned off and `xrWaitFrame` paces drawing; MonoGame
  catches up on updates when frames take longer, so simulation speed doesn't depend on the headset's refresh
  rate. A running session also counts as window focus, so a headset user never sees MonoGame's inactive
  slowdown or a deaf UI.
- **Comfort is decided from what the Sim does, not from which action it is.** The camera watches three signals:
  - whether direct control is on top;
  - how far the Sim's position has moved since you last had them;
  - how high their head is above the floor (relative to their standing height), and whether it has settled.

  This catches routing, sitting, kneeling, lying and stairs without a list of interactions. A blink holds the rig
  still while it fades out and makes its move at full black.
- **The vignette is a ring in view space**, sized in degrees from the eye's forward direction. It is therefore
  round and centred whatever the headset's (asymmetric) field of view, and only motion of the rig counts:
  physically walking around never darkens the view.
- **sRGB swapchains with a raw copy.** MonoGame renders gamma-space colours into RGBA8 targets, and copying
  those bits unconverted into an `SRGB8_ALPHA8` swapchain gives the correct result.

### Tests

- `tools/vr/layout-check/run.sh` compares every hand-written OpenXR struct's size and field offsets with the
  Khronos C headers (165 checks).
- `tools/vr/math-check` (`dotnet run -c Release`) checks the buy/build cursor maths without a GPU:
  - the shifted projection unprojects the mouse pixel onto the aim point, to within 3 mm;
  - the picking camera reproduces the controller ray;
  - the arc solver lands on a grabbed object;
  - the movement arc reaches about 7.5 m at 45 degrees and lands within its step budget at any pitch.
- `tools/vr/render-check/run.sh [dir]` renders the real vignette and blink overlay (on a headset-like asymmetric
  field of view), the VR menu and the wrist displays to PNG files under Xvfb, to look at without a headset.
- `tools/vr/smoke-test/run-monado.sh` opens a MonoGame DesktopGL window under Xvfb, starts a session on the
  Monado runtime (simulated HMD, null compositor), renders both eyes plus opaque and alpha-blended quads, and
  checks pixel orientation and GL errors. Variables: `SIMITONE_XR_LOADER=direct` exercises the loader-free
  path, `SDL_VIDEO_X11_FORCE_EGL=1` the EGL binding. Ubuntu packages: `monado-service libopenxr1-monado
  libopenxr-loader1 xvfb mesa-vulkan-drivers libvulkan1 libgl1-mesa-dri libsdl2-2.0-0`.

## Known limitations and next steps

- **Untested on real hardware**, including the Steam Frame, and untested with a real lot (needs The Sims). That
  includes Sim mode, pointing, the floating panel, the wrist displays and buy/build mode.
- **Uncertain hardware details:**
  - The Frame controller's face-button paths are inferred from its Touch-like layout. If SteamVR rejects them,
    it falls back to a reduced binding set and then to the Touch bindings (logged in `vr.log`).
  - The wrist display offsets are estimates and may need tuning per controller.
- **Comfort tuning is untested in a headset.** The thresholds below are first guesses from the research in
  [VR_COMFORT.md](VR_COMFORT.md) and need tuning in play:
  - when to step out (moved 0.6 m without you steering);
  - what counts as settled (1 s still) and as lying (head below 55% of standing height);
  - the 4 m leash, the blink timing and the vignette strengths.

  The movement arc queues a "Go Here" like a floor click, so two arcs in a row queue two walks. The Sim's real walk
  and run speeds in VR haven't been measured yet.
- **The VR menu isn't in the game's Options panel yet** (only in the headset, on the wrist, or F9), and there is
  no first-run comfort wizard.
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

**Buy/build mode: next steps.**
- **Catalogue:** it is the 2D catalogue on a panel; a VR-native catalogue would be a wrist tablet with 3D
  thumbnails.
- **Feedback:** there is no "pull" animation; the held object snaps from tile to tile at the end of the arc, as it
  does under the mouse. Placement errors play the game's error sound, but the reason only shows as a tooltip on the
  panel.
- **Your Sim's floor only:** in Sim mode you can only build on the floor your Sim is on; switch to observer (B) to
  build elsewhere.
- **Cutaway:** walls stay up while you build, with no cutaway.
