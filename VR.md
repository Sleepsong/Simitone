# Simitone VR (OpenXR)

> **Status: experimental.** Native VR builds and its OpenXR layer passes headless tests against the Monado
> runtime, but it has **not yet been run on a real headset** (including the Steam Frame) or with a real lot
> loaded. Expect rough edges; see [Known limitations](#known-limitations-and-next-steps).

Simitone can be played in VR through OpenXR. **In a lot the house is a dollhouse on a table in front of you**
(by default): you're a giant who moves furniture and Sims about with your hands. Or **be your Sim**: see through
their eyes, walk them around with the thumbstick, and point at things to use them. The clock is on your right
wrist and your needs and the game's speed are on your left.

**Comfort comes first.** When your Sim acts on their own (walking to the fridge, sitting down, going to bed), your
view never slides or bobs about by default: you step out, watch, and follow with quick blinks. Three comfort
levels, a vignette, stick or arc movement and the other options can all be changed in the headset, from the
**VR menu**.

In **buy and build mode** you stay where you are and place things remotely. The catalogue is a tablet in your hand
with the objects in 3D; a beam picks objects up from a distance, and a teleport-style arc shows where they'll land.

In the **dollhouse** you're a giant: the lot is a model on a table in front of you. You move furniture with your
hands (straight off the catalogue, too), pick Sims up and put them down somewhere else, tap them for their pie
menus, lift a floor off like a lid to see the one below, and walk round the table with the walls facing you cut
away. Bring a Sim up to your face to become them.

Everything that isn't the 3D lot (loading screens, the neighbourhood, CAS, options) appears on a flat virtual
screen floating in front of you.

It runs on any OpenXR runtime that supports OpenGL: SteamVR on Windows and Linux, Monado, and natively on the
**Valve Steam Frame** (SteamOS, arm64), standalone or streamed from a PC.

## How it plays

When a lot loads you see it as a **dollhouse** (the default, `PlayMode`; [below](#dollhouse)). B goes on to playing
as your Sim, then to the observer, then back to the dollhouse.

**Play as your Sim.** With `PlayMode=Sim`, B from the dollhouse, or by bringing a Sim up to your face there, you're
put in your selected Sim's head.
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
  look down you see their hands where yours are, pointing the way you point and turned the way you turn your
  wrists. They make a fist while you squeeze the grip or pull the trigger, and the pointing hand points while its
  beam is on something you can use (the game's own hand poses). During an action their hands do the action, and
  the arms blend back to it in a fifth of a second. `SimHands=False` keeps the game's animation.
- **Interacting:** point with the right controller (the laser turns green on something usable) and pull the trigger.
  That object's (or Sim's) pie menu floats over it, turned to face you, at a readable distance and size: the game's
  own buttons and layout, with your Sim's head in the middle turning to look at the choice you point at. Point and
  pull the trigger again to choose; the grip (or the button in the middle) goes back a level, or closes the menu at
  the top; the trigger anywhere else closes it. `VRPieMenus=False` shows the game's 2D pie menu on a panel instead.
  Pointing at the floor gives "Go Here". The Sim walks over and does it, then hands control back to you. What
  your view does meanwhile is up to the comfort setting (below).
- **Questions** (phone calls, job offers, Yes/No) appear on their own on the floating panel at eye level, with a
  buzz in both hands and a sound. If you're mid-blink they wait until it's over.
- **Messages that need no answer** don't pop up in front of you: the watch (on your pointing hand's wrist) buzzes
  and shows a "!" with the title. Look at the watch and pull that hand's trigger to read the oldest one. None is
  ever lost: one left unread for two minutes, or a sixth while five are waiting, pops up as it would have, and all
  of them do if you leave first person. `WatchMessages=False` pops them up straight away.
- **Typing:** whenever a text box is waiting for you (naming a baby or pet, Create-A-Sim names), a keyboard
  appears below you. Point and pull the trigger on the keys.
  - Each word starts with a capital; SHIFT changes the next letter. Hold DEL to keep deleting.
  - HIDE puts the keyboard away until another text box needs it.
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
  - play: play as dollhouse, Sim or observer, extra height (for playing seated), wrist displays, messages on the
    watch, stick when busy, action bubble, buy/build in VR or on the flat screen;
  - *Dollhouse: Fit it to me*, which puts the dollhouse in front of you again at the right height (after sitting
    down, say);
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

<a id="dollhouse"></a>**Dollhouse** (the default when a lot loads; B from the observer; B again goes back to your Sim; or *Play as*
in the VR menu). The lot shrinks to a model about 1.3 m across, its ground a little below your hands, in front of
you; floors above the one shown and the roof are hidden, as in the game. Walk around the table to see it from any
side: the walls facing you are cut away room by room as you go, as the game cuts them (or they're all down, or
all up: the tablet's wall button in buy mode, or *Walls in buy/build* in the VR menu). If it ends up too high or
too low (you sat down), *Dollhouse: Fit it to me* in the VR menu puts it in front of you again.
- **Pointing** works as everywhere: point at a Sim or an object and pull the trigger for its pie menu, or at the
  floor for "Go Here".
- **Touching:** tap a Sim or an object with the tip of your pointing hand (in and straight back out, quickly) for
  its pie menu, as pointing at it does. Your hands give a little tick as they brush past furniture and Sims.
- **Furniture:** put your hand on it and squeeze the grip to pick it up. It's in your hand, with a line down to where
  it would go (green where it fits, red where not); turn your hand to turn it a quarter at a time, and let go to put
  it down. If it can't go there, it goes back where it was and the game says why. The game only lets you move
  furniture in buy mode, which pauses it: during live play it goes into buy mode while you carry something and
  back to live when you've put it down.
- **Sims:** put your hand on a Sim and squeeze to pick them up; they dangle from your hand. Let go over the floor
  where you want them: they stop what they were doing and the game moves them there (The Sims' own way of moving a
  Sim, as its "moveobjects" cheat does). One Sim at a time; not in buy or build mode, where the game is paused.
  They kick their legs while they dangle.
- **Becoming a Sim:** bring the Sim you're holding up to your face and you're them: one of your family becomes
  the selected Sim and you play as them, in first person; anyone else, and you're the observer standing beside
  them. Or put your head into a room of the floor shown (below its ceiling) for half a second, and you're the
  observer standing there. B brings you back to the dollhouse.
- **The catalogue:** in buy mode, put your pointing hand on an item of the tablet and squeeze the grip: it comes
  off the tablet in your hand, and you carry it into the house and let go where it goes, as furniture you've
  picked up (it's bought when it's put down).
- **Lifting a floor off:** take hold of the edge of the floor shown (round its outside, where the walls below it
  end) with a grip and lift: the whole floor comes off like a dollhouse lid, with its walls, furniture and Sims,
  and you see the floor below, which is now the one you play on. Let go of it near its place and it drops back
  on; anywhere else (beside the house, on the table) it stays there until you take it again. Changing floors puts
  it back. (Seeing the lifted floor needs single-pass stereo, which most PC drivers have; without it the floor
  is hidden while it's off.)
- **The model itself:** squeeze a grip in empty space and it moves with your hand (up and down too); with both
  hands, turn them to turn it and move them apart or together to make it bigger or smaller (a tile from 0.75 cm to
  25 cm). The left stick slides it, the right stick turns it (as turning does elsewhere) and right stick up/down
  shows a floor up or down.
- **Buy and build:** the walking hand's stick click goes live → buy → build → live (the grips grab here). The
  catalogue tablet works as usual; what you take from it is placed along a straight beam, not an arc.

**Observer** (B to switch). You leave your Sim and roam freely as yourself:
- smooth locomotion, snap or smooth turning, and right stick up/down to change floors;
- walls and furniture stop you as they stop Sims (you slide along them); doors don't;
- **stairs:** walk onto them and you ride up or down them at walking speed, with the vignette as when the stick
  moves you (`ObserverStairs=Blink` blinks you to the other end instead);
- **teleport:** hold the left trigger, aim the arc and let go to blink there;
- the same pointing and pie menus as in Sim mode, so you can direct Sims like in the normal game;
- **your controllers in your hands** (also when you've stepped out of your Sim): the real ones, as the headset's
  software models them, with their sticks, triggers and buttons moving as you use them. That's SteamVR, on the
  Steam Frame and on a PC, so the Frame's own controllers, Index, Vive or Touch controllers. Where the runtime
  can't provide models, Simitone has its own of Meta's Touch controllers (Rift S and Quest 1, Quest 2, Quest 3 and
  3S, Quest Pro: for Meta's PC runtime over Link) and of Vive wands (for Monado), which move the same way; for
  anything else it draws a simple controller with its green band. `ControllerModels=False` hides them.

**Wrist displays.** Turn your wrist to look at it:
- **right wrist:** a watch made of the game's clock (time and speed) and money display, plus any messages
  waiting;
- **left wrist:** the selected Sim's name, their action queue, their needs, a **VR Options** button and the
  **speed dial**.
  - The speed dial has the game's speed buttons (pause, normal, fast, fastest) on an arc over a knob, its needle
    on the current speed. Point at one and pull the trigger. It's greyed out in buy and build mode, which pause the
    game.
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
- **The catalogue** is a tablet in your walking hand (the left, or the right when left-handed), in Simitone's look:
  - at the top, Live / Buy / Build tabs and your money; then the categories, and the subcategories with their names;
  - then the items, eight to a page, each with its price (red if you can't afford it). Objects are shown in 3D,
    and the one you point at turns, so you can see it from all sides. Build tools keep the catalogue's pictures,
    as do objects whose 3D model isn't ready yet;
  - below them, the name and price of what you're pointing at or holding, and the page buttons.

  Point at it with the other hand and pull the trigger. Choosing an item picks it up (choosing it again puts it
  back); then aim the arc as below. Your needs display makes way for the tablet while it's there.
  `Catalogue=Desk` puts the game's own catalogue on a desk-like panel below you instead.
- **Placing:** what you're holding hangs at the end of an arc from your controller, like a teleport arc. Green
  means it fits, red means it doesn't. Aim and pull the trigger to place it.
  - Right stick up/down throws the arc further or nearer.
  - Right stick left/right turns the object 90 degrees.
- **Moving existing objects:** point the straight beam at an object (green when you're on one) and pull the
  trigger to pick it up, gravity-gun style; it stays where it is until you aim the arc somewhere else.
- **Cancel and sell:** the right grip puts it back or cancels (as Escape does), and B sells or deletes what you're
  holding.
- **Build tools** work the same way: walls, floors, wallpaper, terrain, doors and windows. Pick the tool in the
  catalogue, then aim the arc at the floor; walls and floors are dragged by holding the trigger while you sweep the
  arc.
- **Gliding:** what you hold glides from tile to tile as you sweep the arc, rather than jumping.
- **Picked up from the lot:** the tablet shows what it is and what it sells for, as the game's pickup panel does,
  with **Sell** (or Delete) and **Put back**.
- **Other floors:** the floor buttons at the top of the tablet change the floor you build on at any time, even while
  you hold something or use a tool; with empty hands, right stick up/down does too (so do the game's own floor
  buttons on the desk catalogue). You can go up to floors that aren't built yet: there's no gravity in buy and
  build, so you float there.
- **Walls:** the wall button on the tablet switches, as the game's does, between cut away (the walls within a
  couple of tiles of where you're placing, except the ones right next to you), down and up (`BuildWalls`, also in
  the VR menu).
- **Why not:** when something can't go where you're aiming, the game's reason ("not enough room", "can't afford
  it") floats above it in orange. Build tools' tips (a wall's cost, for example) float there too, in white.

**Travel.** The vacation, downtown and neighbourhood pickers float in front of you on the panel; point and pull the
trigger to choose.

**Sound.** The game's sounds come from where things are, heard from your head: louder as you get closer and quieter
from other floors than yours (the game's own sound model, with you as the listener instead of the camera). Each
Sim's and object's sound is placed in 3D, in the direction it really is from your head, and played through HRTF
(OpenAL Soft's binaural rendering for headphones), so you can tell in front from behind and above from below,
not just left from right. `Sound3D=False` gives the game's left/right panning.

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
| Left grip | Buy mode | Buy mode | Next mode (buy → build → live); the catalogue tablet's tabs do the same | Next mode |
| Left trigger (hold, aim, let go) | Movement arc: your Sim walks there | Teleport there | (observer: teleport) | n/a |
| Left menu button (Touch); left trackpad squeeze (Index); VR Options on the left wrist | VR menu | VR menu | VR menu | VR menu |
| Right stick left/right | Snap / smooth turn | Snap / smooth turn | Turn what you hold; else turn | n/a |
| Right stick up/down | n/a | Go up / down a floor | Arc reach while holding; else floor (any, built or not) | Scroll |
| Right stick click | Cycle game speed | Cycle game speed | n/a (paused) | Cycle game speed |
| Right trigger | Point and interact; click on menus | Point and interact; click on menus | Pick up / place / drag a tool; choose in the catalogue | Left click |
| Right trigger on the left wrist | Cancel the queue icon pointed at, or press VR Options | Same | Same | n/a |
| Right trigger while looking at the watch | Read the oldest waiting message | Same | Same | n/a |
| Left stick held while your Sim is busy | Cancel their actions and take over | n/a | n/a | n/a |
| Right grip | Back a level of a pie menu (closes it at the top); right click on menus | Same | Cancel (put back) | Right click |
| A | Flat screen | Flat screen | Flat screen | First person |
| B | Switch to observer | Switch to the dollhouse | Sell / delete what you hold; else the next of your Sim / observer / dollhouse | Recentre the screen |
| X | Pause / resume | Pause / resume | n/a | Pause / resume |
| Y | Next Sim in the family | Next Sim in the family | n/a | Next Sim in the family |

**In the dollhouse** the controls are the observer's, except:

| Input | Dollhouse |
|---|---|
| Either grip, hand on furniture | Pick it up; turn your hand to turn it; let go to put it down (buy mode while you carry it) |
| Either grip, hand on a Sim (live mode) | Pick them up; let go over the floor to put them down there |
| Either grip, at the edge of the floor shown | Lift it off like a lid; let go near its place to put it back |
| Pointing hand's grip on a catalogue item | Take it off the tablet; carry it in and let go to place it |
| Pointing hand's fingertip, tapped on a Sim or object | Its pie menu |
| A Sim you hold, brought to your face | Become them (your family) or stand beside them (anyone else) |
| Your head in a room for half a second | Stand there, as the observer |
| Either grip, in empty space | Hold the model: it moves with your hand |
| Both grips, in empty space | Turn the model and make it bigger or smaller |
| Left stick | Slide the model |
| Left stick click | Next mode (live → buy → build → live) |
| Right stick up/down | Show a floor up / down |
| B | Back to playing as your Sim |

**Left-handed** (`LeftHanded`; *Hands* in the VR menu, or the second page of the comfort setup): every row above
swaps hands. The left controller points and picks, the right stick walks and its trigger throws the movement arc,
X/Y do what A/B did and the other way round, and the wrist displays swap (the needs display, which you point at,
goes on your right wrist; the watch on your left). The menu button stays on the left Touch controller (the right
one hasn't got one). It applies at once.

Bindings are suggested for the **Steam Frame controllers** (`XR_VALVE_frame_controller_interaction`; that extension
isn't in the public registry, so Simitone asks SteamVR which of the usual button names it has, one at a time, logs
them, and binds each action to the first that's there),
**Oculus Touch**, **Valve Index** (A/B on the walking hand act as X/Y), **HTC Vive** wands (trackpads move and
turn) and the KHR simple controller. SteamVR lets you rebind everything in its controller binding UI; the two
layouts are the action sets "Simitone" and "Simitone (left-handed)".

Keyboard fallback in first person, for testing without controllers: WASD/arrows to move, Shift to sprint,
Q/E to turn, PageUp/PageDown to change floors (observer). F9 opens the VR menu.

## Settings (`vr.ini`)

`vr.ini` is in the Simitone user folder (on Linux `~/Documents/Simitone/`, or `~/.local/share/Simitone/` if there is no
Documents folder; on Windows `Documents\Simitone\`). It lists every option; the ones at their default are
commented out (`# Key=Value`), so they follow the defaults if a later version changes them, and only what you've
changed (in the file, or in the VR menu) is set. Remove the `#` to set one by hand. A `vr.defaults.ini` next to the
executable, if present, supplies defaults of its own; the Steam Frame package ships one.

| Key | Default | Meaning |
|---|---|---|
| `PlayMode` | Dollhouse | `Dollhouse` (the lot as a model on a table), `Sim` (first person as your Sim) or `Observer` (roam freely) when a lot loads |
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
| `PanelDistance` / `PanelWidth` | 1.3 / 1.7 | Floating dialog panel distance and width, metres (the buy/build catalogue desk, with `Catalogue=Desk`, is closer, lower and 90% as wide) |
| `VRBuildMode` | True | Buy and build in first person with remote placement. `False` uses the flat screen instead |
| `Catalogue` | Hand | Buy/build catalogue: `Hand` (a tablet in your walking hand, with 3D objects) or `Desk` (the game's catalogue on a panel below you) |
| `BuildWalls` | Cutaway | Walls in buy/build: `Cutaway` (around where you're placing), `Down` or `Up` |
| `ObserverStairs` | Ride | Observer on stairs: `Ride` up or down them, or `Blink` to the other end |
| `Sound3D` | True | Sims' and objects' sounds placed in 3D around your head, with HRTF. `False`: the game's left/right panning |
| `VRPieMenus` | True | Pie menus drawn for VR (the game's buttons and your Sim's head, sized for a headset). `False` shows the game's 2D pie menu on a panel |
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
3. Make The Sims available on the headset. The Steam version (*The Sims Legacy Collection*) is found
   automatically in any Steam library (internal or SD card, as Steam's `libraryfolders.vdf` lists them): the folder
   its Steam manifest names, or `The Sims Legacy Collection` or `The Sims`, under `steamapps/common` or `common`, with
   the game's `GameData` in it or one folder down. Its neighbourhood (saved in its Proton prefix,
   `compatdata/3314060/pfx/drive_c/users/steamuser/Saved Games/Electronic Arts/The Sims 25/`) is copied to
   Simitone's own the first time, so run the Legacy Collection once beforehand. Otherwise set
   `SIMITONE_SIMS_PATH=/path/to/The Sims` in the launch options, or pass `-path"..."`. `startup.log` in the Simitone
   user folder (`~/Documents/Simitone/`, or `~/.local/share/Simitone/`) says what was found and where it looked.

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
  VRCatalogTablet.cs                  the buy/build catalogue tablet in your walking hand (IVRCatalogSource)
  VRGameCatalog.cs                    the game's catalogue (UIMainPanel, UIBuyBrowsePanel) as the tablet's source
  VRCatalogThumbnails.cs              3D pictures of catalogue objects (their lot meshes, FreeSO's RCObject shader)
  VRPieMenuPanel.cs                   the pie menu drawn for VR (IVRPieSource), and the game's UIPieMenu as its source
  VRAudio.cs                          3D sound: sounds placed around your head; HRTF on MonoGame's OpenAL device
  VRBundledControllers.cs             Simitone's own controller models (for runtimes with none), chosen and animated
  VRCameraController.Dollhouse.cs     the dollhouse camera: the lot as a table-top model, held, turned and resized
  VRDollhouse.cs                      the dollhouse's hands: furniture (through the object holder), Sims, the model
Client/Simitone/Simitone.VR/Controllers/   those models (MIT, @webxr-input-profiles/assets 1.0.20; see its README),
                                      copied next to the game as VRControllers/
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
  same spot, so a held object doesn't fly about while you choose. On the tablet, the cursor camera stops following
  your aim and the mouse stays parked, with its buttons up.
- **The catalogue tablet** drives the game's own catalogue rather than copying it (`VRGameCatalog`): the tabs call
  `UISimitoneFrontend.SwitchMode`, categories `UICategorySwitcher.Select`, subcategories
  `UIBuyBrowsePanel.InitSubcategory` and items `Selected` / `Deselect`, exactly as its buttons do, so the hidden 2D
  catalogue stays in step (`UIBuyBrowsePanel` now says which subcategories it shows and which one is chosen).
- **3D pictures** (`VRCatalogThumbnails`) are each object's mesh from FreeSO's 3D mode (`RCMeshes`, with every tile
  of a multi-tile object), drawn with the lot's own object shader in even white light, from the angle FreeSO's
  object thumbnails use, at twice the size and halved. The shader is shared with the lot, so its lighting and
  matrices are put back afterwards. They're asked for as the tablet updates and drawn between the lot and the UI,
  two a frame; a mesh FreeSO is still reconstructing waits until it's done. The pointed-at one is redrawn each
  frame, turning.
- **VR pie menus** (`VRPieMenuPanel`) lay out the game's open `UIPieMenu` level the way `UIPieMenu.RenderMenu`
  does, with its button texture, colours and fonts, scaled up, and show its Sim head (`UIPieMenu.HeadTexture`),
  whose gaze follows the pointed-at button (`HeadLook`). Choosing goes through `UIPieMenu.Choose`, the same path as
  a click. The 2D menu stays open underneath (on the desktop window) and closes with it.

**The dollhouse** (`VRCameraController.Dollhouse.cs`, `VRDollhouse`) is the same camera at a much bigger scale: the
rig maps your room to the world as world = rig + yaw * (room metres * scale), with about 150 world units per metre
instead of 3, so a tile is about 2 cm and the eyes' separation, the controllers and the pointer all scale with it.
- **Holding the model:** a hand that takes hold pins the world point under it (`VRMath.RigPlacing`); two hands pin
  the point between them, and the vector between them gives the turn about the vertical and the scale
  (`VRMath.TwoHandHold`). Floors, cutaway and the sound centre use the floor point you point at.
- **What a hand is on:** a Sim, as the game's 3D picking sees one (a box around the middle of their body); else the
  first object straight down from just above the hand, within 2.5 cm of it, using the objects' own bounds
  (`ObjectComponent.IntersectsBounds`), which is also what the game's picking finds; else nothing (the model).
- **Furniture** goes through the game's object holder, exactly as buy mode with a mouse: the cursor camera
  (`HandCursor`) looks straight down through the hand while picking, and at the floor below it while carrying; the
  grip is its mouse button (`SimitoneVR.ForcedWorldButton`, applied as the next update's mouse): a press picks up,
  and a press and release put down, each step lasting at least an update. If the holder still holds it afterwards,
  it couldn't go there, and `Cancel` puts it back. In live mode the game is switched to buy mode first (as its mode
  buttons do) and back after; the catalogue tablet stays away meanwhile. While carried, it's drawn lifted to the hand
  (`VRLotWorld.HeldLift`, with the glide), and the placement line runs from the hand to the floor.
- **Sims:** lifting one cancels what they're doing (again every 1.5 s, as free will finds them things to do); they're
  drawn hanging from the hand, out of their seat if they're in one (`VRLotWorld.ApplyLift`, put back after drawing).
  Let go, and they're drawn standing on the tile below until they've stopped (nothing left in their queue, not
  seated; or after 6 s of game time), then `VMNetMoveObjectCmd` moves them: in The Sims 1 it moves Sims too. If they
  aren't there shortly after, it didn't work, and they're shown where they really are. A dangling Sim's legs swing
  from the hips, over the game's animation and for drawing only (`VRSimArms.Kick`, found from the feet up as the
  arms are from the hands).
- **Walls facing you:** the game's own room cut (`VMArchitectureTools.GenerateRoomCut`) for every indoor room of
  the floor shown, with the camera's yaw as the cut rotation, redone when the floor or the side you're on changes
  (and every second), so the walls between you and each room come down as in the game's cutaway mode.
- **Touch** is the same hand test with a reach of 8 mm: entering something gives a haptic tick; a tap is the
  pointing hand going in and out within 0.35 s, ending within 2 cm of where it went in, without squeezing, and opens
  the pie menu as pointing does (`UILotControl` through `OpenPieFor`).
- **The catalogue:** a grip on a tablet item (`VRCatalogTablet.ItemAt`, the hand point on the tablet's plane)
  chooses it, as pointing and pulling the trigger does, and the hand carries the new object from there.
- **Becoming a Sim:** the held Sim's hand within 20 cm of your head: `VMNetChangeControlCmd` for one of the family
  and play mode Sim, or play mode Observer next to them. Leaning in: your head over a floor tile of the floor shown,
  between half a unit above it and 85% of the way to the ceiling, for half a second, makes you the observer there.
- **The lid** (`LidFloor`): taking the edge (a tile of the floor, by its floor or walls, and one without, within
  about 2 cm of the hand, at the floor's height) makes the floor below the one shown, so the game draws, picks and
  cuts walls for that floor; the lifted floor's walls are left whole. After the lot is drawn, `VRLotWorld.DrawLid`
  draws it again with every floor up to the lifted one, from the view moved the opposite way to the lid, without
  clearing or the sky, clipped to what's above the lifted floor's underside. The clip is a clip distance in the
  single-pass stereo shaders: the patch writes `gl_ClipDistance[0] = dot(gl_Position, simitone_clip)` before the
  eye's delta, with the plane in the stereo uniform block, made from the world plane for that pass's view
  (`VRMath.ClipPlane`, in double precision; in clip space the plane is good to about a thousandth of a unit at
  the dollhouse's near plane, and it sits 0.02 units under the floor). `GL_CLIP_DISTANCE0` is on only for that
  draw. What a hand holds below the lid (a Sim, furniture) is hidden from it.
- **The speed dial** asks the bridge for a speed, which sets it as the game's speed buttons do.

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
- **3D sound and HRTF (`VRAudio`).** FreeSO's sound VM already has a 3D path, unused: a listener (`HITVM.Listener`)
  and an emitter per sound (`HITSound.Set3D`), applied with MonoGame's `Apply3D` on every tick instead of the pan.
  Each update, the listener is set to your head and each sound with an owner gets an emitter in that owner's
  direction from your head, 50 units away: inside OpenAL's reference distance (`SoundEffect.DistanceScale`, 100),
  so OpenAL adds direction but no distance attenuation, and the game's own volume, which HIT sets after
  `Apply3D`, stands. Leaving first person removes the emitters (the field is protected: reflection). HRTF is OpenAL
  Soft's `ALC_SOFT_HRTF`, switched on with `alcResetDeviceSOFT` on MonoGame's own device, found by reflection
  (`OpenALSoundController._device`, `MonoGame.OpenAL.AL.NativeLibrary`); MonoGame's OpenAL Soft (1.23) has a built-in
  HRTF data set. When VR stops, it goes back to OpenAL's own choice.
- **Held objects glide (`VRLotWorld.ApplyGlide`).** The object holder puts what you hold on the tile under the
  cursor every update. Only while the eyes are drawn, its parts are shown part of the way there from where they
  were drawn last (closing the gap in about a tenth of a second), and put back straight after, so the game never
  sees them anywhere else.
- **Stair ride.** Walking onto stairs as the observer starts a ride along the line from where you are to just past
  their far end, eased in and out, at walking speed: the rig carries you (you can still move your head) while
  your height goes from one floor's to the other's, and the floor you're on changes halfway. Gravity waits until
  it's over.
- **Hand roll and poses.** Each controller's roll about the way it points (0 with the thumb up) turns the Sim's
  hand the same way about the same line, after it's aimed, so it works without knowing the skeleton's bone axes.
  The hand poses are FreeSO's `SimAvatar.LeftHandGesture` / `RightHandGesture` (the game's open, fist and pointing
  hand meshes), changed only when they differ; the game's own pose is remembered (an animation that changes it
  meanwhile wins) and put back when the arms stop following you.
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
  - a hand without a runtime model gets one of Simitone's own (`VRBundledControllers`), chosen by the interaction
    profile the runtime reports for that hand (`xrGetCurrentInteractionProfile`, on
    `XrEventDataInteractionProfileChanged`) and, for Meta's Touch controllers (one profile for every generation),
    the headset's name. Its parts move as its `profile.json` says (the WebXR motion-controller format: a node moves
    between a min and a max node by a button's value or a stick's position, or shows when touched), fed from the
    game's actions, which are per role and mirrored when left-handed. They're drawn at the grip pose, which the
    WebXR assets are made for;
  - otherwise the built-in one at the grip pose.

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
- **Panels on the headset.** Projection layer plus up to ten quads: the two wrist displays, the floating panel,
  the catalogue tablet, the pie menu, the tip, the keyboard, the menu and the setup.

### Tests

- `tools/vr/layout-check/run.sh` compares every hand-written OpenXR struct's size and field offsets with the
  Khronos C headers (169 checks). With `OPENXR_INCLUDE` set to a folder of newer headers (e.g. OpenXR-SDK's
  `include`, 1.1.4x or later) it also checks the render model structs (203 checks).
- `tools/vr/math-check` (`dotnet run -c Release`) checks the buy/build cursor maths without a GPU:
  - the shifted projection unprojects the mouse pixel onto the aim point, to within 3 mm;
  - the picking camera reproduces the controller ray;
  - the arc solver lands on a grabbed object;
  - the movement arc reaches about 7.5 m at 45 degrees and lands within its step budget at any pitch;
  - single-pass stereo: the shared view contains every point either eye sees, and each eye's clip delta reproduces
    its own projection (three IPDs and cants);
  - foveation: the sharp square follows the gaze, slides back inside the view at the edges, and its pixels line
    up with the eye's;
  - the dollhouse: what one hand holds stays in it as it moves; turning two hands turns the world and what each
    holds stays in it; moving them twice as far apart makes it twice the size; the size limit holds; a 64-tile lot
    fits 1.3 m; the lid's clip plane, for the dollhouse's view moved with the lid, keeps what's above the lifted
    floor (to within a thousandth of a unit).
- `tools/vr/render-check/run.sh [dir]` renders the real vignette and blink overlay, on a headset-like asymmetric
  field of view, to a PNG file under Xvfb.
- `tools/vr/ui-check/run.sh <published Simitone> [dir]` renders every VR panel with Simitone's real UI (its fonts,
  art and controls, through FreeSO's UI layer) and sample game data, to PNG files under Xvfb:
  - the watch and the left wrist display (and pointing at the speed dial's fastest stop and pulling the trigger
    asks for speed 3);
  - the VR menu;
  - three comfort setup pages;
  - the keyboard and the placement tip;
  - the catalogue tablet in buy mode (two items with made-up 3D meshes, one held and one pointed at, one too dear,
    with a description), in build mode choosing a subcategory, and showing an object picked up from the lot;
  - the VR pie menu's top level (with a stand-in for the Sim's head) and a category of eleven choices;
  - the built-in controller model, from the side and from above.

  It also checks the Sim arm pose on a made-up skeleton shaped like The Sims' (23 checks): a lifted Sim's feet
  swing opposite ways from hips that stay put; the wrist reaches a
  target in reach and points at one out of reach, bones keep their lengths, the hand points the way the controller
  does, the elbow bends down and out, folding and blending behave, a skeleton without arms is left alone, and a
  controller's roll is measured about the way it points and turns the hand about that line without moving the
  wrist or fingers (`--arms-only` runs just these).

  And it checks the glTF loader used for the runtime's controller models (10 checks) on a small model made in
  code: nodes, scene, meshes, materials, embedded image, transforms, a bad file rejected, and that a node pose
  moves a part and a hidden node isn't drawn. With `UICHECK_GLB_DIR` pointing at a folder of real controller
  models laid out like the webxr-input-profiles assets' `profiles` folder (MIT; a few are bundled, see below), it also draws
  Index, Touch, Touch Plus and Vive models with their trigger pulled through the same node-pose mechanism.

  Single-pass stereo, on the GPU (Mesa llvmpipe here): every vertex shader (FreeSO's 17 effect files and MonoGame's
  own, 66 shaders) converts; one draw fills both layers, each with its own view; depth comes across; a clip plane
  cuts what's drawn in both layers until it's cleared; normal drawing works afterwards. Then the real renderer on a
  scene of boxes with a headset-like field of view: single pass matches two passes (under 0.01% of pixels differ),
  and foveated rendering matches full resolution where you look and stays within about 1/255 on average elsewhere;
  and the dollhouse lid's way of drawing (everything again from a moved view, clipped above a cut) matches drawing
  the boxes below and the moved boxes above separately, while drawing each eye separately refuses to clip (images:
  `stereo-*.png`).

  The catalogue's 3D pictures, drawn through FreeSO's real object shader with made-up meshes (12 checks): pictures
  are drawn the frame after they're asked for; the render target, viewport and the lot's lighting and matrices are
  put back; the object is drawn whole and framed, with transparent corners; textures use only their unpadded part
  (`UVScale`); a mesh still being made waits and is drawn when done; no mesh means no picture; and the pointed-at
  one turns (images: `thumb-*.png`).

  Simitone's own controller models (3 checks): each headset gets its controllers' model, all ten (five sets, each
  hand) load and draw, and their triggers, grips, sticks and buttons move (image: `controllers-bundled.png`).

  3D sound (7 checks), with OpenAL Soft playing into a WAV file (`run.sh` sets that up): HRTF switches on through
  `VRAudio` on MonoGame's device ("Default HRTF"); a noise burst placed to the left is louder and brighter in the
  left ear, and the other way round; behind you it is duller than in front (HRTF's spectral cue; without HRTF they
  differ only in level); and inside OpenAL's reference distance, where `VRAudio` puts sounds, the level is the
  game's own.

  It needs only Simitone's own `Content` folder from a published build, not The Sims.
- `tools/vr/smoke-test/run-monado.sh` opens a MonoGame DesktopGL window under Xvfb, starts a session on the
  Monado runtime (simulated HMD, null compositor), renders both eyes plus opaque and alpha-blended quads, and
  checks pixel orientation (including a region copy, as the dialog panel uses) and GL errors. Variables: `SIMITONE_XR_LOADER=direct` exercises the loader-free
  path, `SDL_VIDEO_X11_FORCE_EGL=1` the EGL binding, and `SIMITONE_SMOKE_LINKED=1` SteamVR's layout without the
  loader (as on the Steam Frame): `active_runtime.json` a symbolic link to a manifest whose relative `library_path`
  is relative to where the manifest really is. `SIMITONE_XR_REFUSE_ATTEMPTS=n` treats the first n tries at creating
  the instance as refused, and `SIMITONE_XR_PROBE_PROFILE=<profile>` finds another profile's buttons the way the
  Steam Frame controller's are found (on Monado's Touch profile it finds exactly Touch's), to exercise the fallbacks (OpenXR 1.1, then fewer optional extensions) that Simitone
  steps through when a runtime won't create one with everything it asked for. The second half of the run is left-handed, and input must sync
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
- **Popups:** all of Simitone's own popups are full-width dialogs, which show on their own on the panel (also while a
  pie menu is open). The cheat box (Ctrl+Shift+C on the desktop keyboard) isn't shown in the headset.
- **The catalogue tablet and VR pie menus** are tested with sample data, not the game's (that needs The Sims):
  - where the tablet sits in your hand and its tilt are first guesses;
  - the 3D pictures are taken from the angle of FreeSO's own object thumbnails, unchecked against real objects
    (some may show their side or back; the pointed-at one turns anyway);
  - the tablet has no search box (the game's catalogue on the desk has one: `Catalogue=Desk`);
  - the pie menu's Sim head is the game's own render, so it is tested only with a stand-in.
- **The dollhouse** is tested in its maths and builds, not yet in a headset with a real lot (that needs The Sims):
  - picking up furniture relies on the game's picking finding the same object as the hand test, and on its object
    holder behaving with a mouse the same in VR as on the desktop (it already drives VR buy mode);
  - moving Sims uses the command the game uses for moving objects, which in The Sims 1 moves Sims too; whether the
    game accepts every spot (and how a Sim takes being moved mid-action, before they've wound down) is untested;
  - the lot's surroundings and sky are drawn as in first person, so the table stands in the neighbourhood;
  - only one Sim and one piece of furniture can be carried at a time;
  - the lifted floor (the lid) is drawn by drawing the lot a second time, so it costs about that much more while a
    floor is off; it isn't drawn at all without single-pass stereo; its lights are as they were when it came off;
    a roof isn't part of it (the dollhouse hides roofs);
  - touch, leaning in and the lid's edge are judged by distances (8 mm, half a second, about 2 cm) that are guesses
    until they're tried in a headset.
- **Your Sim's hands** are untested with the game's own skeletons (they need The Sims). They rely on the hand
  bones being called `L_HAND` and `R_HAND`; if they aren't, `vr.log` says so and the arms keep their animation.
  The hand poses are the game's three (open, fist, pointing), not each finger, and the arms only follow you while
  you walk your Sim (during an action they do the action).
- **Controller models from the runtime** are untested against a runtime that provides them: Monado 21 (used for
  the tests here) doesn't. The loader is tested on real controller models, and the bindings against the Khronos
  headers. Valve's own OpenXR utilities show the Steam Frame's controllers through the same two extensions
  (`ValveSoftware/Unity`, `ValveOpenXRRenderModelFeature`). Simitone's own models are tested offscreen; picking them
  by the interaction profile is untested on Meta's runtime (it may report a newer Touch profile for some
  controllers, which picks the right model too). Valve Index models aren't included (12 MB; SteamVR provides them).
- **3D sound** is tested by measuring OpenAL Soft's output, not by ear in a headset. Only mono sounds are placed
  (most of the game's); stereo ones play as they are. On the Steam Frame the system's OpenAL is used: if it has no
  HRTF data, the VR menu says HRTF is off and sounds are still placed, with plain stereo panning.
- **The observer stair ride** goes in a straight line from end to end; it doesn't follow stairs that turn.
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
- **Catalogue:** the tablet pages through a subcategory eight at a time; scrolling with the stick and a search
  (with the VR keyboard) would help in big categories.
- **Feedback:** the held object glides between tiles but there is no "pull" animation from where you picked it up.
