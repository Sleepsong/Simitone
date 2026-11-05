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
- **Walking:** the left stick walks them relative to where you look. Walls, doors and stairs are the game's own,
  because this is FreeSO's direct control: the same mechanism the gamepad support uses.
  - **Running:** click the left stick to run; they keep running until you let go of the stick. Running is fast for
    VR (see [Speeds](#speeds)); `RunAtFullTilt=True` makes a fully pushed stick run instead.
  - **Fast game speeds:** if the game runs at speed 2 or 3 when you start walking, it drops to normal speed (your
    Sim would otherwise walk 3 or 10 times as fast), then goes back to your speed 2 seconds after you let go
    (`NormalSpeedWhenSteering`).
- **Turning:** turn with the right stick. Your Sim turns to face where you look.
- **Your own head:** you can lean a little away from the Sim's head, and their head is hidden so you don't see the
  inside of it.
- **Movement arc:** hold the left trigger, aim the arc at the floor and let go: your Sim walks there (it's the
  floor's "Go Here"). Cyan means you can go there, red means you can't. A new arc replaces the walk you sent
  before, rather than queueing another.
- **Taking over:** if your Sim is busy (walking off to do something, sitting, using an object), hold the stick for
  a moment. Everything they're doing or have queued is cancelled; they wind down gracefully, as when you click
  their actions, and then walk where you push. Turn this off with `StickCancelsActions=False`; then holding the
  stick only stands up a seated Sim.
- **Your hands are your Sim's hands:** while you walk your Sim, their arms reach for your controllers, so when you
  look down you see their hands where yours are, pointing the way you point. During an action their hands do the
  action, and the arms blend back to it in a fifth of a second. `SimHands=False` keeps the game's animation.
- **Interacting:** point with the right controller (the laser turns green on something usable) and pull the trigger.
  That object's (or Sim's) real pie menu floats over it, turned to face you, at a readable distance and size;
  point and pull the trigger again to choose. Pointing at the floor gives "Go Here". The Sim walks over and does it, then hands control back to you. What
  your view does meanwhile is up to the comfort setting (below).
- **Questions** (phone calls, job offers, Yes/No) appear on their own on the floating panel at eye level, with a
  buzz in both hands and a sound. If you're mid-blink they wait until it's over.
- **Messages that need no answer** don't pop up in front of you: your right wrist buzzes and the watch shows a
  "!" with the title. Look at the watch and pull that hand's trigger to read the oldest one. Unread messages clear
  after two minutes, and all of them are shown as normal dialogs if you leave first person. `WatchMessages=False`
  pops them up instead.
- **Typing:** whenever a text box is waiting for you (naming a baby or pet, Create-A-Sim names), a keyboard
  appears below you. Point and pull the trigger on the keys.
  - Each word starts with a capital; SHIFT changes the next letter. Hold DEL to keep deleting.
  - HIDE puts the keyboard away until another text box needs it.
  - Name boxes in dialogs start with the current name, so OK on its own keeps it.
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
trackpad. On any controller, press **VR Options** on your left wrist display.
On the desktop keyboard, press F9.
- **Using it:** it's a Simitone dialog with the options in two lists; point at an option and pull the trigger to
  step it to its next value.
- **Options:**
  - comfort: Sim actions, vignette strengths;
  - moving: movement (stick, arc or both), turning (snap 30/45/90 or smooth), walk direction (head or left hand),
    running (click or full tilt), slowing a fast game while you walk;
  - play: play as Sim or observer, extra height (for playing seated), wrist displays, messages on the watch,
    stick when busy, action bubble, buy/build in VR or on the flat screen;
  - render scale;
  - *Comfort setup*, which runs the first-run setup again.
- **Saving:** changes apply immediately and are saved to `vr.ini`.
- **Closing:** pull the trigger beside the menu, press OK, or press the menu button again.
- **In the game's Options panel:** with `-vr`, options mode has a **VR** button next to Free Will. It opens the same
  options as a list you click through (each click steps an option), on the flat screen or on the desktop.

**Comfort setup.** The first time VR starts, a short setup floats in front of you, before or after the lot
loads. It asks:
- what happens when your Sim walks off (Comfortable, Moderate or Intense);
- snap or smooth turning;
- stick, arc or both for moving;
- standing or seated;
- how strong the vignette is.

Each choice applies at once, and the current one is highlighted. NEXT and BACK move between pages (the grip also
goes back), and START at the end saves it (`ComfortSetupDone=True`). Run it again from the VR menu or the Options
panel's VR button.

**Observer** (B to switch). You leave your Sim and roam freely as yourself:
- smooth locomotion, snap or smooth turning, and right stick up/down to change floors;
- walls and furniture stop you as they stop Sims (you slide along them); doors don't;
- **stairs:** walk onto them and you blink to the other end, on the other floor;
- **teleport:** hold the left trigger, aim the arc and let go to blink there;
- the same pointing and pie menus as in Sim mode, so you can direct Sims like in the normal game;
- **your controllers in your hands** (also when you've stepped out of your Sim): the real ones, as the headset's
  software models them, with their sticks, triggers and buttons moving as you use them. That's SteamVR, on the
  Steam Frame and on a PC, so the Frame's own controllers, Index, Vive or Touch controllers. Where the runtime
  can't provide models, Simitone draws a simple controller with its green band. `ControllerModels=False` hides
  them.

**Wrist displays.** Turn your wrist to look at it:
- **right wrist:** a watch made of the game's clock (time and speed) and money display, plus any messages
  waiting;
- **left wrist:** the selected Sim's name, their action queue, their needs and a **VR Options** button.
  - The queue uses the game's own queue icons: the running action is big and ringed, with its name underneath, and
    a cancel mark shows one being cancelled.
  - Point the right controller at an icon and pull the trigger to cancel it, as clicking it in the game's queue
    does.
  - The needs are the game's need bars, Hunger to Bladder on the left and Energy to Room on the right, as in The
    Sims.

**Action bubble.** While you're stepped out of your Sim, what they're doing floats over their head, drawn as the
game's queue icon. Point at it and pull the trigger to cancel it. `ActionBubble=False` turns it off.

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
- **Other floors:** right stick up/down changes the floor you build on (while you're not holding anything or using a
  tool). You can go up to floors that aren't built yet: there's no gravity in buy and build, so you float there.
- **Cutaway:** the walls within a couple of tiles of where you're placing are cut away, as the game does around the
  mouse, except the ones right next to you.
- **Why not:** when something can't go where you're aiming, the game's reason ("not enough room", "can't afford
  it") floats above it in orange. Build tools' tips (a wall's cost, for example) float there too, in white.

**Travel.** The vacation, downtown and neighbourhood pickers float in front of you on the panel; point and pull the
trigger to choose.

**Sound.** The game's sounds come from where things are, heard from your head: louder as you get closer, panned by
which way you face, and quieter from other floors than yours (the game's own sound model, with you as the
listener instead of the camera).

**Tips.** Whatever the game shows as a tooltip beside the mouse floats over what you're pointing at instead: a
Sim's name, an object's sale price, why something can't be placed.

**Flat screen.** A (or Tab) switches between first person and the normal game on a floating screen, where the
laser pointer is your mouse. Options mode switches to the flat screen automatically and comes back to first
person when you're done. With `VRBuildMode=False`, buy and build use the flat screen too.

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

## Speeds

How fast your Sim moves, worked out from FreeSO's engine at normal game speed (the simulation runs 30 ticks a
second) and the default `WorldScale` (one tile is one metre):

| Movement | Speed | Where it comes from |
|---|---|---|
| Walking them with the stick | about 1.7 m/s | direct control's top speed: 0.9 sub-tiles (1/16 tile) a tick |
| Running them (stick click) | about 5.1 m/s | three times the acceleration with the same friction: 2.7 sub-tiles a tick |
| Walking somewhere on their own | about 1.5 m/s | routing: 8/10 of a sub-tile a tick |
| Game speed 2 / 3 | 3x / 10x the above | the simulation ticks 3 or 10 times as often |

A comfortable walking pace in VR is about 1.4 m/s and running about 3 m/s.
- **Walking** is close to that, so it stays as it is.
- **Running** is fast, so it only happens when you click the stick.
- **Fast game speeds** drop to normal while you walk.
- **Gliding** after your Sim (Moderate) is set a little above their own walking pace, so you keep up.

While you play, `vr.log` records what your Sim actually did, as lines like
`speed: walk 1.68 m/s over 4.2 s (7.1 m) at game speed 1`. Each line covers a stretch of 1.5 s or more of walking,
running or routing. If you share a log from a headset session, the defaults can be tuned from it.

## Controls

| Input | First person: Sim | First person: observer | Buy / build (first person) | Flat screen |
|---|---|---|---|---|
| Left stick | Walk your Sim | Move | (observer: move) | n/a |
| Left stick click | Run (until you let go of the stick) | Sprint (hold) | (observer: sprint) | n/a |
| Left grip | Buy mode | Buy mode | Next mode (buy → build → live) | Next mode |
| Left trigger (hold, aim, let go) | Movement arc: your Sim walks there | Teleport there | (observer: teleport) | n/a |
| Left menu button (Touch); left trackpad squeeze (Index); VR Options on the left wrist | VR menu | VR menu | VR menu | VR menu |
| Right stick left/right | Snap / smooth turn | Snap / smooth turn | Turn what you hold; else turn | n/a |
| Right stick up/down | n/a | Go up / down a floor | Arc reach while holding; else floor (any, built or not) | Scroll |
| Right stick click | Cycle game speed | Cycle game speed | n/a (paused) | Cycle game speed |
| Right trigger | Point and interact; click on menus | Point and interact; click on menus | Pick up / place / drag a tool; choose in the catalogue | Left click |
| Right trigger on the left wrist | Cancel the queue icon pointed at, or press VR Options | Same | Same | n/a |
| Right trigger while looking at the watch | Read the oldest waiting message | Same | Same | n/a |
| Left stick held while your Sim is busy | Cancel their actions and take over | n/a | n/a | n/a |
| Right grip | Close a pie menu; right click on menus | Close a pie menu; right click on menus | Cancel (put back) | Right click |
| A | Flat screen | Flat screen | Flat screen | First person |
| B | Switch to observer | Switch to playing as your Sim | Sell / delete what you hold; else switch Sim / observer | Recentre the screen |
| X | Pause / resume | Pause / resume | n/a | Pause / resume |
| Y | Next Sim in the family | Next Sim in the family | n/a | Next Sim in the family |

**Left-handed** (`LeftHanded`; *Hands* in the VR menu, or the second page of the comfort setup): every row above
swaps hands. The left controller points and picks, the right stick walks and its trigger throws the movement arc,
X/Y do what A/B did and the other way round, and the wrist displays swap (the needs display, which you point at,
goes on your right wrist; the watch on your left). The menu button stays on the left Touch controller (the right
one hasn't got one). It applies at once.

Bindings are suggested for the **Steam Frame controllers** (`XR_VALVE_frame_controller_interaction`),
**Oculus Touch**, **Valve Index** (A/B on the walking hand act as X/Y), **HTC Vive** wands (trackpads move and
turn) and the KHR simple controller. SteamVR lets you rebind everything in its controller binding UI; the two
layouts are the action sets "Simitone" and "Simitone (left-handed)".

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
| `GlideSpeed` | 1.6 | Moderate: the steady speed (m/s) you glide after your Sim at (Sims walk on their own at about 1.5) |
| `BlinkDuration` | 0.25 | Length of a blink (fade out, move, fade in), seconds. 0 makes moves instant |
| `StickCancelsActions` | True | Holding the stick while your Sim is busy cancels their actions so you can walk them. `False`: it only stands up a seated Sim |
| `ActionBubble` | True | While you're stepped out, show your Sim's current action over their head (point and pull the trigger to cancel it) |
| `WatchMessages` | True | In first person, messages that need no answer wait on your watch instead of popping up |
| `RunAtFullTilt` | False | Playing as your Sim, a fully pushed stick runs. `False`: walk, and run after clicking the stick |
| `NormalSpeedWhenSteering` | True | Walking your Sim at game speed 2 or 3 drops the game to speed 1 until you've let go for 2 s |
| `SimHands` | True | While you walk your Sim, their arms reach for your controllers. `False`: their arms always animate as usual |
| `ControllerModels` | True | Show your controllers in your hands when you're not in your Sim's body (the runtime's own models when it has them, else a simple one) |
| `ComfortSetupDone` | False | The first-run comfort setup has been done; set `False` to see it again |
| `RenderScale` | 1.0 (Frame: 0.75) | Multiplier on the headset's recommended per-eye resolution |
| `WorldScale` | 3.0 | FreeSO world units per metre. 3 = life size (a TS1 tile is about 1 m) |
| `MoveSpeed` | 1.6 | Observer walking speed, m/s |
| `SprintMultiplier` | 2.5 | Observer speed multiplier while sprinting |
| `MoveDirection` | Head | `Head` or `LeftHand`: what "forward" on the stick means (`LeftHand` is the hand holding that stick: the right one when left-handed) |
| `LeftHanded` | False | Point with the left controller and walk with the right (see [Controls](#controls)) |
| `SinglePassStereo` | True | Draw the lot once for both eyes (OpenGL multiview) when the graphics driver can; `False` draws each eye separately |
| `Foveation` | Off (Frame: Light) | `Off`, `Light` (70% resolution outside a 48 degree centre) or `Strong` (50% outside 40): the centre follows your eyes when the headset tracks them |
| `TurnMode` | Snap | `Snap` or `Smooth` |
| `SnapTurnDegrees` | 30 | Snap turn angle |
| `SmoothTurnDegreesPerSecond` | 120 | Smooth turn speed |
| `WallCollision` | True | Observer: walls and furniture stop thumbstick movement (doors don't; stairs take you to the other floor) |
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
- **OpenGL multiview** (`GL_OVR_multiview2`, with `GL_ARB_shading_language_420pack`) for single-pass stereo, which
  Mesa offers on most drivers. If the Frame's driver doesn't, `vr.log` says "single-pass stereo unavailable" and
  each eye is drawn separately.
- **Eye tracking** through `XR_EXT_eye_gaze_interaction`, which is how Valve exposes the Frame's eye tracking to
  apps. If SteamVR asks whether apps may use your eye data, allow it for foveated rendering to follow your eyes;
  otherwise it stays centred (`vr.log` says which).
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
| Holding the stick cancels what my Sim is doing | That's the take-over; set `StickCancelsActions=False` (VR menu: *Stick when busy*) |
| A message buzzed but nothing appeared | It's on your watch: look at it and pull the right trigger. Or set `WatchMessages=False` |
| No way to open the VR menu on your controller | Press VR Options on your left wrist display (needs `WristDisplays` not `Off`), or F9 |
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
  VRUIPanel.cs                        a panel of Simitone UI floating in the room (a UIExternalContainer)
  VRUIControls.cs                     Simitone-style building blocks: dialog frame, choices, keys, headset icon
  VROptions.cs                        the option list shared by the VR menu and the Options panel
  VRMenu.cs                           the VR options menu (live apply, saved to vr.ini)
  VRSetup.cs                          the first-run comfort setup
  VRKeyboard.cs                       pointer keyboard for whatever text box is waiting
  UIVROptionsDialog.cs                the VR options in the game's own UI (Options panel, VR button)
  VRGameBridge.cs                     direct control of your Sim, pointing -> pie menus, buy/build mode, flat screen,
                                      movement arc -> "Go Here"
  VRUILayer.cs                        captures the game UI into its own texture for the floating panel
  VRWristHud.cs                       watch (and waiting messages), needs and action queue displays
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
- **The VR panels are Simitone UI.** The menu, the comfort setup, the keyboard and the wrist displays are
  `UIExternalContainer`s:
  - FreeSO's UI layer updates them and renders each into its own texture with its own input, using Simitone's
    fonts, art (dialog stripes and title bars, outlined boxes, big buttons, the clock, money, need bars and queue
    icons) and controls (touch lists, buttons);
  - the controller's ray is each panel's mouse, so buttons, lists and queue icons work as they do with a mouse;
  - `VRUIPanel` copies the texture into a swapchain and submits it as a quad layer.
- **Popups stay the game's own.** VM dialogs are still the game's `UIMobileAlert`s, so:
  - the panel shows just the band of the window the dialog covers (Simitone's dialogs span its width), copied
    with `GLInterop.BlitTextureRegion`;
  - plain messages are the same alerts, held back by `UILotControl` and shown later when you read them on the
    watch;
  - the keyboard types through `UITextEdit.PadType` / `PadBackspace`, the on-screen keyboard helpers from the
    gamepad work, into the focused text box (or the one in the top dialog).
- **Releasing the trigger.** After the VR menu, the keyboard or a wrist display uses a press, the trigger and grip
  must be released before they click anything else, so the press that closes the menu never clicks what's behind
  it.
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
- **Your Sim's arms (`VRSimArms`).** Each frame the game animates the skeleton (`VM.PreDraw` → `ReloadSkeleton`,
  which makes a new matrix array), then, before the eyes are drawn, each arm is posed with two-bone IK:
  - The hand bone (`L_HAND` / `R_HAND`) is the wrist. The elbow and shoulder are the first two bones above it that
    are some way apart, so an extra bone in the chain doesn't break it. The elbow bends down and out.
  - The rotations are applied to the skinning matrices and to the bones' absolute positions, so what the Sim holds
    (slot `R_FINGER0`) stays in their hand. If the game hasn't re-animated the skeleton (paused), the animated pose
    is restored first rather than bent twice.
  - The skinning matrices are a protected field of FreeSO's `Avatar`; FreeSO is a submodule, so they're reached by
    reflection rather than by changing it.
  - The wrist target is 8 cm behind the grip pose along the aim pose's forward direction (the way your hand
    points), and the hand points that way too.
- **Sound.** FreeSO's 3D sound volume is distance from the camera's target (and zoom), pan is from the camera's
  look direction, and sounds are quieter on floors other than the visible one. The VR camera puts its target just
  in front of your eyes with a fixed close zoom, and while sounds update (`VM.PreDraw`) the visible floor is set
  to yours without redrawing anything (`WorldState.SilentLevel`), then put back for drawing.
- **Observer collision.** Furniture is the game's own footprints for objects that don't let people through, as
  FreeSO's direct control decides it, except doors and stairs. Stairs are a multitile object with portal parts on
  two floors.
- **Controller models (`VRRenderModels`, `GltfModel`, `VRControllerRenderer`).** With `XR_EXT_render_model` and
  `XR_EXT_interaction_render_model` (Valve's recommended way to show the Steam Frame's controllers), the runtime
  lists the devices you hold, each with a glTF model:
  - when the runtime reports the devices changed (an event during `xrSyncActions`, and once after the first sync),
    the models are enumerated, and each one's GLB and the names of its moving parts are read once, by asset UUID;
  - every frame each model's space is located (only drawn when position and orientation are tracked), its moving
    parts' poses and visibility are read, and `xrGetRenderModelPoseTopLevelUserPathEXT` says which hand it's in;
  - the GLB is parsed on a worker thread (`System.Text.Json`, in .NET) and put on the GPU on the game thread: base
    colour factor and texture, embedded images through MonoGame's `Texture2D.FromStream`, the node tree, and
    the runtime's node poses replacing those nodes' own transforms, as the extension specifies;
  - a hand without a runtime model gets the built-in one at the grip pose.

  The extensions are newer than OpenXR 1.0.20, so the layout check needs newer Khronos headers for them (below).
- **Single-pass stereo (`GLMultiview`, `VRStereoRenderer`).** FreeSO's effects are compiled MonoGame effects
  (MojoShader GLSL) and know nothing of stereo, so it's done underneath them:
  - every vertex shader MonoGame has (FreeSO's and its own) gets a second variant, made at runtime: GLSL 1.30 with
    `GL_OVR_multiview2`, its attribute locations pinned to the original's (MonoGame caches them per shader), and
    one line added before MonoGame's clip-space fix-ups, `gl_Position += delta[gl_ViewID_OVR] * gl_Position`,
    with each eye's delta (minus identity) in a uniform block;
  - FreeSO draws the lot once, for a view that encloses both eyes (`VRMath.Enclose`: facing between them, a field
    of view covering both, set back until both eyes' viewpoints are inside), so its culling, sorting and draw calls
    happen once; each eye's delta is `VRMath.ClipDelta`, which turns that view's clip position into the eye's own,
    exactly;
  - for that pass, MonoGame's shaders are switched to their variants (its program cache is swapped for a second
    one) and a two-layer framebuffer is bound under the render target MonoGame thinks it's drawing to; afterwards
    each layer's colour and depth are copied into its eye's target.

  This uses MonoGame 3.8 DesktopGL internals by reflection; if anything is missing (a field, a GL extension, a
  shader that won't convert) it falls back to one pass per eye and says why in `vr.log`. Anything drawn in screen
  space during the lot pass would be offset in the second eye; FreeSO's 3D lot pass has none.
- **Foveated rendering.** Each eye is drawn twice: the whole view into a smaller render target, and a square around
  your gaze at the eye's own pixel density, snapped to the eye's pixels so it lines up exactly. The sharp square is
  blended over the soft view with a feathered edge (a mask written into its alpha, then SpriteBatch; no custom
  shaders), and both depths are copied in for what's drawn on top. The gaze is the point 2 m along
  `XR_EXT_eye_gaze_interaction`'s gaze, seen from each eye; through a blink the square stays put, and it eases back
  to the middle if tracking is lost. With single-pass stereo the frame is two lot passes; without, four.
- **Once-per-frame animation.** FreeSO eases some things while drawing (an avatar settling its tilt on sloped
  ground, Mario) by `WorldState.FramePerDraw`, worked out once per frame; only the first lot pass of a frame gets
  it, so extra passes don't speed them up.
- **Hands.** Two action sets carry the same actions with mirrored bindings, both attached; the one for your
  handedness is the one synced and read. A third set has the hands' poses, haptics and eye gaze. Haptics are
  addressed by role (pointing hand, walking hand), so they follow.
- **Panels on the headset.** Projection layer plus up to eight quads: the two wrist displays, the floating panel,
  the tip, the keyboard, the menu and the setup.

### Tests

- `tools/vr/layout-check/run.sh` compares every hand-written OpenXR struct's size and field offsets with the
  Khronos C headers (167 checks). With `OPENXR_INCLUDE` set to a folder of newer headers (e.g. OpenXR-SDK's
  `include`, 1.1.4x or later) it also checks the render model structs (203 checks).
- `tools/vr/math-check` (`dotnet run -c Release`) checks the buy/build cursor maths without a GPU:
  - the shifted projection unprojects the mouse pixel onto the aim point, to within 3 mm;
  - the picking camera reproduces the controller ray;
  - the arc solver lands on a grabbed object;
  - the movement arc reaches about 7.5 m at 45 degrees and lands within its step budget at any pitch;
  - single-pass stereo: the shared view contains every point either eye sees, and each eye's clip delta reproduces
    its own projection (three IPDs and cants);
  - foveation: the sharp square follows the gaze, slides back inside the view at the edges, and its pixels line
    up with the eye's.
- `tools/vr/render-check/run.sh [dir]` renders the real vignette and blink overlay, on a headset-like asymmetric
  field of view, to a PNG file under Xvfb.
- `tools/vr/ui-check/run.sh <published Simitone> [dir]` renders every VR panel with Simitone's real UI (its fonts,
  art and controls, through FreeSO's UI layer) and sample game data, to PNG files under Xvfb:
  - the watch and the left wrist display;
  - the VR menu;
  - three comfort setup pages;
  - the keyboard and the placement tip;
  - the built-in controller model, from the side and from above.

  It also checks the Sim arm pose on a made-up skeleton shaped like The Sims' (18 checks): the wrist reaches a
  target in reach and points at one out of reach, bones keep their lengths, the hand points the way the controller
  does, the elbow bends down and out, folding and blending behave, and a skeleton without arms is left alone
  (`--arms-only` runs just these).

  And it checks the glTF loader used for the runtime's controller models (10 checks) on a small model made in
  code: nodes, scene, meshes, materials, embedded image, transforms, a bad file rejected, and that a node pose
  moves a part and a hidden node isn't drawn. With `UICHECK_GLB_DIR` pointing at a folder of real controller
  models laid out like the webxr-input-profiles assets' `profiles` folder (MIT; not included), it also draws
  Index, Touch, Touch Plus and Vive models with their trigger pulled through the same node-pose mechanism.

  Single-pass stereo, on the GPU (Mesa llvmpipe here): every vertex shader (FreeSO's 17 effect files and MonoGame's
  own, 66 shaders) converts; one draw fills both layers, each with its own view; depth comes across; normal drawing
  works afterwards. Then the real renderer on a scene of boxes with a headset-like field of view: single pass matches
  two passes (under 0.01% of pixels differ), and foveated rendering matches full resolution where you look and
  stays within about 1/255 on average elsewhere (images: `stereo-*.png`).

  It needs only Simitone's own `Content` folder from a published build, not The Sims.
- `tools/vr/smoke-test/run-monado.sh` opens a MonoGame DesktopGL window under Xvfb, starts a session on the
  Monado runtime (simulated HMD, null compositor), renders both eyes plus opaque and alpha-blended quads, and
  checks pixel orientation (including a region copy, as the dialog panel uses) and GL errors. Variables: `SIMITONE_XR_LOADER=direct` exercises the loader-free
  path, `SDL_VIDEO_X11_FORCE_EGL=1` the EGL binding. The second half of the run is left-handed, and input must sync
  in both layouts. Ubuntu packages: `monado-service libopenxr1-monado
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
  - when to step out (moved 0.6 m without you steering; your own walk through a door doesn't count);
  - what counts as settled (1 s still seated, 3 s standing with no walk to come);
  - what counts as lying (head below 50% of standing height, back up above 62%);
  - the 4 m leash, the blink timing and the vignette strengths.

  The speeds in [Speeds](#speeds) are worked out from the engine; `vr.log` records the real ones while you play.
- **Popups, actions and typing:** only Simitone's full-width dialogs, pie menus and the travel pickers are
  cropped on the panel; anything else shows with the whole UI.
- **Pie menus** float at the object, but they are the regular 2D menus on a panel.
- **Your Sim's hands** are untested with the game's own skeletons (they need The Sims). They rely on the hand
  bones being called `L_HAND` and `R_HAND`; if they aren't, `vr.log` says so and the arms keep their animation.
  The hand points the way yours does but doesn't copy its roll, the fingers keep their animation, and the arms
  only follow you while you walk your Sim (during an action they do the action).
- **Controller models from the runtime** are untested against a runtime that provides them: Monado 21 (used for
  the tests here) doesn't. The loader is tested on real controller models, and the bindings against the Khronos
  headers. Valve's own OpenXR utilities show the Steam Frame's controllers through the same two extensions
  (`ValveSoftware/Unity`, `ValveOpenXRRenderModelFeature`); runtimes without them (e.g. Monado 21) get the
  simple built-in controller.
- **Sound** is the game's stereo panning by direction and distance, not 3D (HRTF) audio.
- **Observer stairs** take you from end to end in a blink; you don't climb them.
- **Performance** is unmeasured on the Frame:
  - single-pass stereo is tested on Mesa's llvmpipe, not on the Frame's driver (Zink on Turnip); if that lacks
    multiview it falls back to two passes;
  - foveation is the renderer's own (two images blended), since Valve's foveated rendering for the Frame
    (`XR_FB_foveation` with fragment density maps) is for Vulkan apps; without single-pass stereo it costs CPU (four
    lot passes) to save GPU;
  - eye-tracked foveation is untested with a real eye tracker (Monado here has none); the centre fallback is what
    the tests cover;
  - the Steam Frame defaults are a guess.
- Windows VR is implemented (WGL binding; loader or registry runtime lookup) but untested.

**Buy/build mode: next steps.**
- **Catalogue:** it is the 2D catalogue on a panel; a VR-native catalogue would be a wrist tablet with 3D
  thumbnails.
- **Feedback:** there is no "pull" animation; the held object snaps from tile to tile at the end of the arc, as it
  does under the mouse.
- **Changing floors** needs empty hands: put down what you hold, or leave the tool, before right stick up/down.
- **Cutaway** follows where you place; the game's wall modes (walls up, down, cutaway) aren't offered in VR.
