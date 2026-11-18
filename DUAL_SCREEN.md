# Two screens (AYN Thor)

On Android devices with a second screen, such as the **AYN Thor**, Simitone puts the lot on the main (top) screen
and the lot's controls on the second (bottom) screen. On a phone with one screen nothing changes.

This is the first version. It needs testing on a Thor, and `simitone.log` says what happened (see below).

## What this branch includes

`engine/dual-screen` is the AYN Thor build: the Android port (`engine/android`) with these merged in:
- the controller port (`engine/controller-support`);
- remesh packs (`prog/meshreplace`);
- the TS1 sound fixes (`fix/sound-fixes`);
- sinks cut into counters in 3D (`fix/sink-basins`);
- doors in 3D walls following the 2D rule (`fix/door-cutaway`);
- fast-forward while the household is away (`fix/auto-fast-forward`);
- travel to the expansion packs' lots (`fix/expansion-travel`);
- upstream's `simitone-forked` as of 12 November 2026 (the catalog index and whole-catalog search among it).

Each has a FreeSO side on `simitone/dual-screen`. The code is the same as the local Android combo build, plus the two
screens and the bottom screen's window and drawing.

## Using it

1. Open Simitone on the **top** screen. In AYN's launcher you can long-press Simitone and set *Screen Launch* to
   *Top Screen*. Simitone only uses a second screen that's no bigger than the one it's on (unless the screens are
   swapped: see **Swap screens and TV mode** below).
2. On a lot, the bottom screen shows:
   - along the top: the money, the floor (down, its name, up), the cutaway button and the clock. Tap the clock for
     the speed buttons. Each tap of the cutaway button goes to the next wall view: cut away, walls up, roof, walls
     down. Choosing a floor (here, or with the D-pad) stops the camera following the Sim, so it stays on that floor;
     R3 follows them again. In direct control the view still moves to the Sim's floor when they take the stairs.
   - in live mode: the family (tap a Sim to control them; the one you control is outlined), then the controlled
     Sim's pages: **Needs** (two columns, as in the original game), **Job**, **Personality**, **Relationships** and
     **Inventory**. Tap a page's name to show it, or turn the pages with the controller's LT and RT. Under them, the
     Sim's actions: the running one large, then the queued ones. Tap one to cancel it.
   - in buy and build mode: the catalog. Its categories run along the top (buy: seating, surfaces, appliances and
     so on; build: walls, floors and roofs; outdoors; doors, windows and more), the open one lit. Under them, the
     category's kinds of item, then its items, in two rows: drag them sideways to scroll. Tap an item to buy it
     (or take the build tool), then place it on the lot on the top screen, at the circle in the middle.
     The picked item's name and price show under the items, with **Back** to go back to the kinds of item, and
     under them its description and what it does for your Sims' needs. With an object picked up off the lot, it
     shows the object's name and what it sells for, with **Sell** and **Put back**; anything in hand can be put back
     from here. The top screen's own catalog and info panels stay hidden in buy and build mode, so nothing covers
     the spot where the item goes.

     **Search**: the magnifier after the categories searches every item for the mode, as you type. The search box
     takes the categories' place, and a keyboard takes the place of the item info and the mode buttons: tap the
     keys, or use the controller as on the top screen's keyboard (D-pad to move, A to type, B to delete, Y space, X
     shift, Start or Done to put it away). Each result says which category it's from. Picking one puts the keyboard
     away so you can place it; tap the search box to type again, its x to clear it, and **Close** (or a category)
     to end the search.
   - **Live**, **Buy**, **Build** and **Options** along the bottom. Buy and Build show their catalog here; Options
     opens its panel on the top screen, as the mode button does on a phone. Tap the lit one again to go back to
     Live. In live mode, Live goes back to the needs. Apart from Options, the top screen's panel stays closed,
     however it was opened, so the top screen shows just the lot.
3. The top screen leaves out its own clock, money, cutaway, mode buttons, Sim panels, catalog and action queue
   while the bottom screen shows them. Its touch buttons for placing objects and using build tools (rotate, the
   tool buttons) hide while the controller is in use, and come back when you touch the top screen.
   Pie menus, dialogs and the keyboard stay on the top screen.
4. The Thor's built-in controls work the lot on the top screen, as with any controller (the controller port,
   `engine/controller-support`, is merged in; its controls are listed in `UILotControlPadHelper`). What they
   change shows on the bottom screen too: the mode (Select), the speed (D-pad left/right), the walls (L3) and the
   Sim (LB/RB). Holding Select opens the mode menu on the top screen. In buy and build mode the controller uses the
   bottom screen's catalog: LT moves the controller between the lot and the catalog, where its focus is outlined;
   the D-pad moves over the kinds of item, then over the items (left/right along the rows, up/down between them);
   A opens a kind or picks an item, which puts it in hand on the lot; B goes back to the kinds of item, then to the
   lot; RT opens the next category; Y searches (B ends the search). The highlighted item's name and price show
   under the items. In live mode LT and RT turn the bottom screen's pages (needs, job, personality, relationships,
   inventory), and **Y** moves the controller to the bottom screen's controls, starting on the Sim's actions (or
   the page's name, with none queued). There the D-pad moves between everything on the bottom screen (the floor,
   cutaway and clock buttons, the family, the pages' names, what's on the page, the actions and the mode buttons),
   outlined, and A presses it: on the actions, left and right move along them and A cancels one; on the clock, A
   opens the speeds and picks one. LT and RT still turn the pages and LB and RB still switch Sims. B (or Y) goes
   back to the lot (on the clock's speeds, B closes them first). Touching the bottom screen doesn't take the
   controls away from the game: its window can't take the focus.
5. In the neighbourhood, the top screen shows just the map, and the bottom screen its name and buttons: **Create a
   family**, **Options**, **Back to the neighborhood** and the other neighbourhoods (Downtown, Vacation Island,
   Studio Town, Magic Town). Pick a house on the map and its panel opens on the bottom screen (Enter Lot or Move
   In, and More for evicting), with the house in the middle of the map.
6. In Create a Family, the top screen shows just the lot and the Sims, and the bottom screen each step's panel,
   with **Back** and **Accept** (**Move in**, **Done**) under it:
   - choosing a family: the families (tap one to pick it), with **New family** and **Delete** beside them;
   - a new family: its last name along the top and its Sims along the bottom (+ adds one; tap one to edit or
     delete them), the family standing in the house on the top screen;
   - making a Sim: the Sim's panel (name, gender, age, skin, personality, bio). The head and body carousels stay
     on the top screen, in the 3D view.
   They slide in and out on the bottom screen as they do on the top one. Tap a name or the bio, on either screen,
   and a keyboard comes up on the bottom screen to type it (on Android there was no keyboard for touch before).
7. With the controller, the two screens are one: down from the top screen's lowest house, or from the Sims, carries
   on to the bottom screen's buttons and panels, and up comes back, with the focus outlined on whichever screen it
   is on. A text box on the bottom screen brings up its keyboard for the pad too (A types, B deletes, Y space,
   X shift, Start done); one on the top screen uses the top screen's pad keyboard, as before.
8. Away from a lot, the neighbourhood and Create a Family, the bottom screen shows the title.

**Options > Second Screen** (Graphics tab, only on devices with a second screen) turns it off and back on without
a restart. Off, the bottom screen goes back to whatever was there, and the controls return to the top screen.

## Swap screens and TV mode

**Options > Swap Screens** (Graphics tab, under Second Screen) puts the game on the other screen and the controls
on the device's own:
- on the Thor, the lot on the bottom screen and the controls on the top one;
- on a phone with a TV or monitor plugged in (USB-C or HDMI), the lot on the TV and the controls on the phone:
  **TV mode**. Use a controller for the lot; the phone's screen is the controls, held sideways.

It swaps at once, in the middle of a game, and back again when turned off. Plugging a TV in while it's on puts the
game there; unplugging it brings the game back to the device's screen, with the controls on it, as on any phone. The
game is laid out again for the other screen's shape, as when a desktop window is resized: on the Thor's bottom screen
it's narrower (1240x1080), so wide dialogs have less room. A TV gets the game at the device's screen height (1080 on
the Thor and most phones), scaled to fit, so a 4K TV doesn't make the phone draw at 4K. A TV has no touch: use the
controller, and the phone's screen for the controls. The controls stay where the controller reaches them (Y in live
mode), but with the controller the second screen still counts as *below* the game, even on the Thor swapped, where
it's above.

How it works: the game never moves. Simitone's activity stays on the device's screen, so the controller's buttons
always reach it, but `SecondScreenHost` resizes its view to the other display's shape (at the device's screen
height, so the UI keeps its size) and covers it with a layer for the controls. After each frame, the game's finished
image is copied into a texture (in the game's own OpenGL context, putting its texture and framebuffer bindings back)
and drawn on the window on the other display, which would otherwise show the controls; that window's touches go to
the game's view, rescaled. The game takes the new size as MonoGame's resize: its buffer size is set to the view's
before the view changes, so it isn't letterboxed. The log says `screens swapped: the game at WxH on display N`,
`copying the game's frames`, `first game frame on the other display`, and `screens back the usual way round`.

This needs testing on a Thor and with a phone and a TV. If the other display stays black or shows the controls
while swapped, the log says why (for example `copying the game's frame failed`).

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
  - `UIDualNeighbourhood` is the neighbourhood's: its buttons press the map's own (`UINeighbourhoodSwitcher`, hidden on
    the top screen), and the house panel (`UIHouseSelectPanel`) is laid out for the bottom screen and opened there.
  - `UIDualCAS` is Create a Family's: it takes the families, family and Sim panels (`UIFamiliesCASPanel`,
    `UIFamilyCASPanel`, `UISimCASPanel`) from the top screen while it shows, the first two laid out for the bottom
    screen (`SetLayout`) and scaled to fit, and its Back and Accept press the top screen's.
  - In live mode, `UIDualDeck` is a pad scope of its own: `UILotControlPadHelper` runs its navigator while the pad is
    there (Y), its page names are pad targets, and its action queue is one target that moves along the actions.
  - `UIDualKeyboard` is the bottom screen's keyboard (catalog search, names): the pad keyboard's layout without the
    number row, for touch and the pad.
  - The pad moves between the screens through two hooks in FreeSO's `UIFocusNavigator` (`ToNavSpace` and
    `DrawsHere`): for choosing where a direction goes, the bottom screen's elements count as below the top screen,
    and an outline on the bottom screen is drawn there.
- `Simitone.Android/SecondScreenHost.cs`:
  - It watches the displays. It shows the window, which can't take the focus, so the built-in controls and keys stay
    with the game, and it queues the window's touches for the game thread.
  - After each frame, on the game thread, it makes a small second OpenGL context current on the second screen's
    surface. That context shares the game's textures, so it can draw the texture over the surface and swap. Then
    the game's context is made current again. The game's own GL state is never touched (apart from the frame copy
    while swapped, which puts it back), and the second screen doesn't wait for its display's refresh.
  - Swapped, it does the same for two surfaces: the game's copied frame on the other display, and the controls on
    the layer over the game's view (see **Swap screens and TV mode**).
- **Options > Second Screen** is `DualScreen` in `config.ini` (on by default); **Swap Screens** is `DualScreenSwap`
  (off by default).

`Simitone.Android/tools/second_screen_check.py` runs the same drawing sequence with Mesa's GLES 3 (surfaceless EGL,
so no screen is needed): `python3 -I second_screen_check.py`. It finds the game context's config by its ID, makes a
context sharing it, and draws a texture from the first context with these shaders and this quad. It checks the
image's top lands at the top, and that the first context is current again afterwards. `swap_check.py` does the same
for swapped screens: it copies a frame out of the first context's own surface (first with `glCopyTexImage2D`, then
`glCopyTexSubImage2D`) and draws it on a surface twice the size, checking the frame's top lands at the top and that
the first context's texture and framebuffer bindings are as they were.

## Not yet

- Swapping the screens and TV mode haven't been tried on a device yet (see above).
- With the screens swapped on the Thor, the controller still counts the controls as below the game.
- Pie menus, dialogs and the top screen's keyboard stay with the game's screen.
