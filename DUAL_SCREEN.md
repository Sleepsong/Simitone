# Two screens (AYN Thor)

On Android devices with a second screen, such as the **AYN Thor**, Simitone puts the lot on the main (top) screen
and the lot's controls on the second (bottom) screen. On a phone with one screen nothing changes.

This is the first version. It needs testing on a Thor, and `simitone.log` says what happened (see below).

## What this branch includes

`claude/dual-screen` is the AYN Thor build: the Android port (`claude/android`) with these merged in:
- the controller port (`claude/intelligent-cray-f9z43d`);
- remesh packs (`claude/meshreplace`);
- the TS1 sound fixes (`claude/sound-fixes`);
- sinks cut into counters in 3D (`claude/sink-basins`);
- doors in 3D walls following the 2D rule (`claude/door-cutaway`);
- fast-forward while the household is away (`claude/auto-fast-forward`);
- travel to the expansion packs' lots (`claude/expansion-travel`).

Each has a FreeSO side on `simitone/dual-screen`. The code is the same as the local Android combo build, plus the two
screens and the bottom screen's window and drawing.

## Using it

1. Open Simitone on the **top** screen. In AYN's launcher you can long-press Simitone and set *Screen Launch* to
   *Top Screen*. Simitone only uses a second screen that's no bigger than the one it's on.
2. On a lot, the bottom screen shows:
   - the money, the cutaway button and the clock along the top. Tap the clock for the speed buttons. Each tap of
     the cutaway button goes to the next wall view: cut away, walls up, roof, walls down.
   - the family. Tap a Sim to control them; the one you control is outlined.
   - the controlled Sim's needs, in two columns as in the original game.
   - the controlled Sim's actions, under the needs: the running one large, then the queued ones. Tap one to cancel
     it. The top screen only shows its own copy while the controller is using it (Y).
   - **Live**, **Buy**, **Build** and **Options** along the bottom. Buy, Build and Options open their panel on the
     top screen, as the mode button does on a phone. Tap the lit one again to close it and go back to Live. From
     those, Live goes back to live mode; in live mode it opens or closes the Sim's panels (needs, relationships,
     job, personality, inventory) on the top screen. However you get back to live mode (the controller's Select
     too), the top screen's panel closes, so the top screen shows just the lot.
3. The top screen leaves out its own clock, money, cutaway, mode buttons and action queue while the bottom screen
   shows them.
   Pie menus, dialogs and the keyboard stay on the top screen.
4. The Thor's built-in controls work the lot on the top screen, as with any controller (the controller port,
   `claude/intelligent-cray-f9z43d`, is merged in; its controls are listed in `UILotControlPadHelper`). What they
   change shows on the bottom screen too: the mode (Select), the speed (D-pad left/right), the walls (L3) and the
   Sim (LB/RB). Holding Select opens the mode menu on the top screen. Touching the bottom screen doesn't take the
   controls away from the game: its window can't take the focus.
5. Away from a lot (neighbourhood, Create-a-Sim), the bottom screen shows the title.

**Options > Second Screen** (Graphics tab, only on devices with a second screen) turns it off and back on without
a restart. Off, the bottom screen goes back to whatever was there, and the controls return to the top screen.

## If it doesn't work

Every display and every step goes to `Download/Simitone/simitone.log`, on lines starting `second screen:`:

- one line per display: size, density, refresh rate, state, flags, and which one Simitone is on;
- the window: `window on display N (WxH), as a presentation`, or as an activity (see below), or why not;
- `surface WxH`, `drawing with a context sharing the game's`, and `first image shown`;
- `system bars left/top/right/bottom`: any bars Android keeps on the second screen (the Thor's bottom screen has its
  own navigation bar). Simitone asks to hide them and keeps its drawing clear of any that stay;
- from the game: `second screen: 1240x1080 px, UI scale 1.93` and `showing the controls`.

The window on the second screen is a **Presentation**, a dialog of Simitone's activity shown on the other display, as
the dual-screen emulators use. If Android won't put one there, Simitone opens a small activity on that display
instead. An empty file named `secondscreen-activity` in Download/Simitone makes it always use the activity.

If drawing fails, the log says why, the second screen is left alone until Simitone restarts, and the controls go
back to the top screen.

### Testing on a phone

Developer options > **Simulate secondary displays** adds a second display that floats over the phone's screen. Or,
to match the Thor's bottom screen:

    adb shell settings put global overlay_display_devices 1240x1080/420

That shows the drawing and layout, but simulated displays don't take touches. To remove it:

    adb shell settings put global overlay_display_devices null

## How it works

- `Simitone.Client/UI/DualScreen`:
  - `SecondScreen` holds the second-screen state. It works with any platform host (`ISecondScreenHost`): the host
    finds the screen, passes on its touches and shows its images.
  - `UISecondScreen` is everything on the second screen. It's one of FreeSO's `UIExternalContainer`s, so the UI
    layer updates it with its own input and draws it into its own texture. It redraws at up to 30 frames a second,
    and at once on a touch.
  - `UIDualDeck` is the lot's controls. It reuses Simitone's own controls: the clock, money, Sim buttons, needs bars
    and mode icons. The UI scale is the phones': the short side is about 560 UI pixels, 1.93 on the Thor.
  - `UISimitoneFrontend` hides the top screen's copies while the second screen is active.
- `Simitone.Android/SecondScreenHost.cs`:
  - It watches the displays. It shows the window, which can't take the focus, so the built-in controls and keys stay
    with the game, and it queues the window's touches for the game thread.
  - After each frame, on the game thread, it makes a small second OpenGL context current on the second screen's
    surface. That context shares the game's textures, so it can draw the texture over the surface and swap. Then
    the game's context is made current again. The game's own GL state is never touched, and the second screen
    doesn't wait for its display's refresh.
- **Options > Second Screen** is `DualScreen` in `config.ini` (on by default).

`Simitone.Android/tools/second_screen_check.py` runs the same drawing sequence with Mesa's GLES 3 (surfaceless EGL,
so no screen is needed): `python3 -I second_screen_check.py`. It finds the game context's config by its ID, makes a
context sharing it, and draws a texture from the first context with these shaders and this quad. It checks the
image's top lands at the top, and that the first context is current again afterwards.

## Not yet

- The buy and build catalogues and tools are still on the top screen. Putting them on the bottom screen is next.
- Create-a-Sim and the neighbourhood don't use the bottom screen yet.
- The controller doesn't reach the bottom screen's buttons yet (everything there can also be done with the pad).
- Swapping the screens, and a phone with a bigger external screen (lot on the TV, controls on the phone).
