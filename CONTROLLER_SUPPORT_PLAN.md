# Controller Support Plan (PS2-style)

**Status:** Phases 1–7 are done. The engine changes are on `simitone/controller-phase7` in `Sleepsong/FreeSO` (which includes Phases 1–6), and the submodule points at it (see §10–§16). Phases 1–6 have been tested on hardware (§11–§15). Phase 7 hasn't been yet.
**Scope:** the Simitone client (`Client/Simitone`) and the FreeSO engine submodule (`FreeSO/TSOClient`).

---

## 1. Target experience

Here, "PS2-style" means the *control model* of the 2003–2004 console Sims games. It does not mean their art, menus or exclusive game modes.

| Pillar | What it means in Simitone |
|---|---|
| The camera follows the Sim | In live mode the camera is anchored to the active Sim by default. The right stick pans away from the Sim and one button snaps back. |
| Two live-mode schemes | **Cursor mode:** the left stick moves a world-space selector that snaps to objects. **Direct control:** the left stick walks the Sim, and the best object in front of them becomes the target. |
| Stick-driven interaction menu | You pick from the pie menu by pushing the stick in a direction, not with a pointer. |
| No pointer required anywhere | Every panel, dialog and catalog can be reached with D-pad focus. |
| Centered build cursor | Buy and build modes place at the screen centre, and the stick scrolls the lot under it. |

**Non-goals for v1:** reproducing PS2 UI art or menus, PS2-exclusive modes, split-screen 2-player (the input layer should not rule it out), and an in-game rebinding UI (overrides go in a config file).

---

## 2. What the codebase gives us today

### 2.1 Input pipeline (engine)

- `tso.common/Rendering/Framework/GameScreen.cs` `Update()` builds the frame's `UpdateState` from either `Mouse`/`Keyboard` or `TouchPanel` (when `FSOEnvironment.SoftwareKeyboard` is set). Every input becomes one or more `MultiMouse` entries. **`UpdateState` holds no gamepad state at all.**
- `FSO.UI/Framework/UIElement.cs:496` hit-tests `ListenForMouse` regions against every `MultiMouse`. `FSO.UI/UILayer.cs:284` only dispatches when `MouseOverWindow` is true. `tso.common/.../IO/InputManager.cs` `HandleMouseEvents` sends over/out/down/up events to the top-most region.
- Keyboard focus (`IFocusableUI`) exists only for text input. **There is no directional focus or navigation concept.**
- A `UIButton` click only fires from mouse events (`FSO.UI/Controls/UIButton.cs` ~325–365). There is no public way to trigger a click.

### 2.2 Lot interaction (Simitone)

- `Simitone.Client/UI/Panels/UILotControl.cs` is driven by screen position throughout:
  - `ShowPieMenu(Point)` (line 427) picks the object with `World.GetObjectIDAtScreenPos` (a GPU pick) and `EstTileAtPosWithScroll`, then places the menu at the mouse.
  - Hover and tooltips (`LiveModeUpdate`, line 559) and the cutaway (`UpdateCutaway`, line 1044) read `state.MouseState` directly.
  - A right-click on a family member switches Sims (line 931). A middle-click rotates the camera (line 966).
- `UILotControlTouchHelper.cs` already shows how an alternate input "helper" can sit beside `UILotControl`. It turns gestures into calls to `TargetZoom`, `World.Scroll`, rotation and `ShowPieMenu`.
- Touch mode already uses a **screen-centre cursor** for the build tools (`UILotControl.cs:1013`) and the buy-mode holder (`UIObjectHolder.cs:361`). `SimulateMD`/`SimulateMU` (`UILotControl.cs:403–413`) already let non-mouse code drive object placement.
- `UIPieMenu.cs` `RenderMenu()` lays items out at **2, 4 or 8 compass slots**. More than 8 items are stacked above and below, and a back button sits in the centre. These slots map directly onto stick directions.

### 2.3 Camera (engine)

- `WorldState.ScrollAnchor` (`tso.world/WorldState.cs:178`) together with `World.CenterTo` (`World.cs:491`, called every update at line 590) is a working follow-cam. Simitone currently only ever *clears* it.
- 3D cameras use `CameraController3D.RotationX/RotationY/Zoom3D`. In 2D, `TS1GameScreen.Rotation` snaps the rotation and `UILotControl.TargetZoom` handles zoom (smooth zoom is already implemented).
- `World.Transform(Vector2)` converts a screen-relative direction into tile space for the current rotation. TSO direct control uses it.
- Screen→tile conversion exists (`EstTileAtPosWithScroll*`). **There is no general tile→screen helper that works in both 2D and 3D.**

### 2.4 Gamepad precedent

`tso.world/Components/SM64Component.cs:433` already calls `GamePad.GetState(0)`. The MonoGame `GamePad` API therefore links and runs in this build, and no new dependency is needed.

### 2.5 Direct control already exists in the VM

FreeSO already contains a complete direct-control implementation. It was built for a TSO event and Simitone does not use it:

- **`tso.simantics/Engine/VMDirectControlFrame.cs`** is a stack frame that moves an avatar from a direction + intensity input. It handles acceleration and friction, wall sliding, stand/walk/run animation blending and client-side prediction. It also handles **portals**: walking into a door or stairs runs that object's portal entry point (entry point 15, line 403).
- **It yields whenever another interaction is queued** (line 637), and `VMThread.EvaluateQueuePriorities` re-queues it at idle priority (`EnsureDirectControlAction`, `VMThread.cs:1053`). That makes "walk up to the fridge and press A" fit naturally: the queued interaction runs with normal routing, then direct control resumes.
- It is driven by two commands, `VMNetDirectControlCommand` and `VMNetDirectControlToggleCommand`. The reference driver is `tso.client/UI/Panels/UILotControl.cs:980–1110`, which shows how keyboard input becomes `VMDirectControlInput` through `World.Transform`.

**Blockers for using it in TS1:**

1. `EnsureDirectControlAction` returns early when `VM.GlobTS1` is set (`VMThread.cs:1056`).
2. It needs the semi-global BHAV `9001` (`0x2329`) on the avatar. That BHAV came from TSO content and does not exist in TS1's person semi-globals. All it does is call Look Towards in mode 255 (`VMLookTowards.cs:63`), which pushes the frame.
3. Both commands' `Verify` methods require the TSO `aprilfools` tuning, which TS1 doesn't have. Simitone runs a `VMServerDriver` (`TS1GameScreen.cs:580`), and that driver calls `Verify` (`VMServerDriver.cs:300`). The commands would be **silently dropped**.
4. The toggle stores its enable flag in `PersonData[54]` (`UnusedAndDoNotUse2`). Person data is saved with the lot, so this would write a TSO-only flag into TS1 save data.
5. Movement is limited to the avatar's current room plus portals. That is fine for houses but matters on large outdoor community lots.
6. `TryMove` has two small bugs (lines 277–289):
   - The `steps = 0` branch is immediately overwritten because an `else` is missing.
   - The velocity threshold is `< 25000` for X but `< 2500` for Z. The asymmetry is almost certainly a typo.

### 2.6 Repo logistics

`FreeSO` is a git submodule. It originally pointed at the `alex-forked` branch of `alexjyong/FreeSO` and now points at the fork `Sleepsong/FreeSO`. Engine work goes on branches in that fork, based on upstream `alex-forked`. Don't base it on the fork's `master`: that is original FreeSO and lacks this project's weather, catalog and floor fixes. Engine changes are still kept small and generic, so they stay easy to upstream.

---

## 3. Architecture

```
MonoGame GamePad.GetState                                   [engine: tso.common]
          │
          ▼
UpdateState.Pad  — current/previous, edge + repeat, radial dead zones,
                   LastInputDevice
          │
          ▼
InputActions map — Confirm, Cancel, Nav*, CameraRotate*, Zoom*, …  [engine: tso.common]
          │
  ┌───────┴─────────────────┬──────────────────────────────┬──────────────────────────┐
  ▼                         ▼                              ▼                          ▼
Focus navigator        Lot pad helper                 Pie menu pad nav          Virtual cursor
[engine: FSO.UI]       [Simitone]                     [Simitone]                [engine: GameScreen]
D-pad between          follow camera, cursor mode,    stick angle → slot        synthetic MultiMouse
INavigable elements;   direct control, sim cycling,                             moved by the stick;
Confirm = click        buy/build centre cursor                                  fallback for screens
                                                                                not yet ported
```

**Principles**

- The gamepad is a **parallel** input source. Lot gameplay never goes through the mouse path. The virtual cursor is only a stopgap for screens that haven't been ported yet.
- `LastInputDevice` decides which UX is active: the mouse cursor, or the selector plus button prompts. Touching the mouse switches back instantly, with no restart and no settings toggle.
- Engine changes stay generic, so any FreeSO frontend could use them. The PS2-specific opinions live in Simitone.

---

## 4. Engine work items (FreeSO)

**E1 — Gamepad state in `UpdateState`** (`tso.common/Rendering/Framework/Model/UpdateState.cs`, `GameScreen.cs`)
- Add:
  - `GamePadState PadState` and `PreviousPadState`.
  - `List<Buttons> NewButtons`, with the same initial-delay/repeat logic used for `NewKeys`.
  - Radial dead zones on both sticks.
  - `InputDevice LastInputDevice`.
- Poll in `GameScreen.Update` only while the window has focus. Use the first connected index found via `GamePad.GetCapabilities`.
- Make no per-frame allocations (reuse the lists).

**E2 — Action map.** A small static class that maps `Buttons` to semantic actions, with optional JSON overrides in the user directory. Keyboard bindings can feed the same actions later.

**E3 — Virtual cursor fallback.**
- When enabled, the left stick moves a `MultiMouse` with a reserved ID, and A acts as its left button.
- Force `MouseOverWindow` to true while it is active (`UILayer.cs:284`).
- This makes every screen minimally usable from day one.

**E4 — Focus navigation** (`FSO.UI`)
- Add an `INavigable` interface (or a `UIElement.Navigable` flag) and a `UIFocusNavigator`. In the pressed direction, the navigator picks the nearest navigable element inside the top-most focus scope, scoring candidates by cone and distance on their screen-space bounds.
- Focus scopes push and pop with `UIScreen.GlobalShowDialog`/`RemoveDialog`, so modal dialogs trap focus.
- Add a public `UIButton.PerformClick()`. The navigator draws a single focus-highlight style, so individual controls don't need their own.
- Containers can override navigation for grids and lists (the catalog, list boxes).

**E5 — World helpers** (`tso.world/World.cs`)
- Add `Vector2? ProjectToScreen(Vector3 tilePos)` that works in both 2D and 3D. In 3D it uses `Viewport.Project` with the `State.Camera` View/Projection; in 2D it uses `WorldSpace.GetScreenFromTile` plus the scroll offset.
- It is needed for selector markers, button prompts and anchoring the pie menu at an object.

**E6 — Direct control for TS1** (`tso.simantics`)
- Replace the `GlobTS1` early-out with an explicit opt-in (for example `VM.AllowDirectControl`, set by Simitone).
- Make `Verify` pass when `vm.TS1 && AllowDirectControl`, and leave TSO behaviour unchanged.
- For TS1, store the per-avatar enable flag in a runtime field on `VMAvatar` instead of `PersonData[54]`.
- Provide routine 9001 by shipping a PIFF in `Simitone.Client/Content/TS1Patch/` that adds BHAV `0x2329` to the person semi-global. The PIFF format supports `Add` entries (`tso.files/Formats/IFF/Chunks/PIFF.cs`).
  - This beats generating the routine in C#. Queued actions are saved by routine ID plus owner GUID, so a routine that only exists in memory would not resolve when the save is loaded.
- Fix the two `TryMove` bugs from §2.5.
- Cancel the direct-control action before `TS1GameScreen.Save()`, so saves never contain a `VMDirectControlFrame`.

---

## 5. Simitone work items

**S1 — `UILotControlPadHelper`** (a sibling of `UILotControlTouchHelper`, owned by `UILotControl`)
- **Camera:**
  - By default, set `World.State.ScrollAnchor` to the active Sim.
  - The right stick pans the camera and detaches the anchor. R3 recentres and re-anchors.
  - The triggers drive `TargetZoom`.
  - Rotation uses `RotationX` in 3D. In 2D it uses `TS1GameScreen.Rotation`, with a repeat delay.
- **Cursor mode:**
  - The selector is a **world-space** point in tile coordinates. The left stick moves it through `World.Transform`.
  - Each frame, score the nearby entities (distance to their footprint, whether they have interactions) and snap the selector to the best one. **No per-frame GPU pick.**
  - Pushing the selector to the edge of the screen moves the camera.
- **Direct control mode:**
  - The left stick feeds `VMDirectControlInput`, with the direction computed through `World.Transform` (same as the TSO client).
  - The Sim runs when the stick is deflected past about 0.9, and walks below that.
  - The target is the best-scored interactable within about 2 tiles, weighted by its angle from the Sim's facing.
- **Confirm:** on a target, Confirm opens the pie menu. On empty floor in cursor mode, it issues Go Here via `GotoObject`.
- Refactor `ShowPieMenu(Point)` into `ShowPieMenuFor(VMEntity, Vector2 screenPos)` so the mouse and pad paths share it.
- Make `UpdateCutaway` take a focus point (the mouse tile, the selector tile or the Sim's tile) instead of reading `state.MouseState` directly.

**S2 — Pie menu.**
- The stick angle picks the nearest of the 2/4/8 slots. Reuse `PieButtonHover` for the highlight and head-turn.
- Confirm clicks the slot. Cancel calls `BackButtonPress`, or closes the menu at the root. LB/RB page through items beyond 8.
- A small hold threshold stops a flick from mis-selecting.

**S3 — Live HUD.**
- Cycle Sims across `CurrentFamily.RuntimeSubset`, reusing the logic at `UILotControl.cs:931`.
- Change speed with the existing `ChangeSpeedTo`.
- Queue focus: select an item and Confirm to cancel it via `VMNetInteractionCancelCmd`.
- Bind mode switching (Live/Buy/Build/Options) to a button.
- Make the motive, relationship and job subpanels navigable via E4.

**S4 — Buy/Build.**
- Reuse the touch-mode centre-cursor code path, but enable it when the pad is active instead of when `SoftwareKeyboard` is set.
- The left stick scrolls the lot.
- Buttons:

| Button | Action |
|---|---|
| LB/RB | `ObjectHolder.RotateObject` |
| A | `SimulateMD` / `SimulateMU` |
| Hold A | Drag tools (walls, floors) via `CustomControl.MouseDown/Up` |
| B | `Cancel` |
| Y | Sell |

- The catalog grid (`UIBuyBrowsePanel`) gets container-level navigation.

**S5 — Dialogs.**
- The `UIMobileAlert` buttons become navigable, with A/B mapped to the default and cancel buttons.
- Text entry (`VMDialogType.TextEntry`, naming, catalog search) needs an on-screen keyboard. Until then, Steam Deck users can rely on the system keyboard.

**S6 — Out-of-lot screens.** The neighbourhood screens (`UINeighbourhoodSelectionPanel`, `UIHouseSelectPanel`) and CAS (`TS1CASScreen`) get navigation via E4. The virtual cursor covers them until then.

**S7 — Settings and prompts.**
- New settings in `GlobalSettings`: enabled, default live scheme, invert Y, dead zone, cursor speed, and button-glyph set (Xbox/PlayStation/Nintendo).
- Prompt glyphs must be original or permissively licensed art. No console-game assets.

**S8 — UI layout decision.**
- The mobile layout (`UIModeSwitcher`, big buttons, centre cursor) is closer to a console layout than `UIDesktopUCP`.
- However, `TS1GameScreen.Desktop = !FSOEnvironment.SoftwareKeyboard` (`TS1GameScreen.cs:47`) ties the layout to the touch input path. Separating layout from input source has to happen first if we want the mobile layout for controller play.

---

## 6. Default mapping

This is in the spirit of the console games. It is not a verified reproduction of any one title's layout. Phase 2 changed the camera and trigger rows from the original proposal; see §11.

**Live mode**

| Input | Cursor mode | Direct control |
|---|---|---|
| Left stick | Move the selector | Walk relative to the camera (run past ~0.9 deflection) |
| Right stick | ↑↓ zoom, ←→ rotate | same |
| A / Cross | Interact, or Go Here on empty floor | Interact with whatever is just in front of the Sim; nothing if there's nothing there |
| B / Circle | Cancel / close | Cancel / close |
| X / Square | Switch to direct control (3D camera only; in 2D a hint says so) | Back to cursor mode (the selector starts at the Sim) |
| Y / Triangle | Focus the action queue | Focus the action queue |
| LB / RB (L1 / R1) | Previous / next household Sim | same |
| LT / RT (L2 / R2) | Needs panel: closed → open → focused → closed / next panel tab | same |
| D-pad ← / → | Game speed down / up | same |
| D-pad ↑ / ↓ | Floor up / down | same |
| L3 | Cycle walls (up / cutaway / down) | same |
| R3 | Recentre and re-anchor on the Sim | same |
| Start | Pause / resume | same |
| Back / Select | Next mode (Live → Buy → Build → Options) | same |

**Pie menu:** the stick points at a slot; the D-pad and LB/RB step through every item, including overflow items and Back; A chooses; B goes back a level or closes.

**Buy/Build** (as built in Phase 5; see §14):

| Input | On the lot | In the catalog |
|---|---|---|
| Left stick | Scroll the lot under the centre cursor | Move (like the D-pad) |
| A | Pick up / place; hold to drag a wall or floor tool | Choose (the pad goes to the lot to place it) |
| B | Put back what you hold, or put the tool away, and go to the catalog | Items → subcategories → lot |
| LB / RB | Rotate what you hold | — |
| Y | Sell what you hold; hold with a tool for its erase/delete form (Ctrl) | — |
| X | Eyedropper (nothing held); hold with a tool for its room/fill form (Shift) | — |
| LT | Into the catalog | Out of the catalog |
| RT | Next category | Next category |
| D-pad ↑ / ↓ | Floor up / down | Move |

---

## 7. Phases

| Phase | Deliverable | Done when |
|---|---|---|
| 0 ✅ | This plan, decisions in §8, FreeSO fork | Decisions recorded |
| 1 ✅ | E1, E2, E3 | <ul><li>Every screen can be reached with a pad via the virtual cursor.</li><li>Mouse/keyboard behaviour is unchanged.</li><li>Switching device mid-game works.</li></ul> |
| 2 ✅ | S8 (mobile layout without touch input); E5; S1 (camera + cursor mode); S2; S3 (Sim cycling / speed / mode) | A household can play a full day in live mode without touching the mouse |
| 3 ✅ | E4; S3 (panels); S5 | <ul><li>Every live-mode panel and VM dialog can be navigated.</li><li>The virtual cursor is no longer needed inside a lot.</li></ul> |
| 4 ✅ | E6; S1 (direct control, 3D camera only) | <ul><li>The Sim can be walked through doors and stairs and can interact.</li><li>Autonomy doesn't fight the stick.</li><li>Save and load round-trip cleanly.</li></ul> |
| 5 ✅ | S4 | Buy, place, rotate, sell, walls, floors and wallpaper all work with a pad only |
| 6 ✅ | S6, S7, on-screen keyboard | Neighbourhood → CAS → lot works without a mouse |
| 7 ✅ | UI polish and bug fixes (added after Phase 6 testing) | <ul><li>Focus and prompts look and read like a console Sims game.</li><li>The neighbourhood view lays out correctly at any window size.</li></ul> |

Phase 1 is the right first PR. It is small, stays inside the engine, is testable, and everything else depends on it.

---

## 8. Decisions (recorded)

1. **Where FreeSO changes land:** your fork, `Sleepsong/FreeSO`.
2. **Default live scheme:** Cursor mode.
3. **Layout under controller:** the mobile layout. Because of this, separating layout from touch input (S8) moves into Phase 2, since the Phase 2 lot work is built on that layout.
4. **Direct control:** 3D camera only in v1.

---

## 9. Risks and things to verify early

- **Autonomy vs direct control.** The frame yields whenever any non-idle action is queued (`VMDirectControlFrame.cs:637`). If free will queues something for the controlled Sim, the player loses control mid-walk. Phase 4 doesn't suppress it explicitly; see §13 for why it's expected not to happen and what to check.
- **BHAV 9001 via PIFF.** Superseded: Phase 4 builds the routine in code instead (§13).
- **`PersonData[54]` in TS1.** Resolved: TS1 uses a runtime field instead (§13).
- **Windows backend.** Windows defaults to WindowsDX (`Simitone.Windows/Program.cs:52`). Its `GamePad` support is expected to be XInput-only, so PlayStation pads may need Steam Input or DS4Windows there; DesktopGL (SDL2) should recognise them natively. Verify on hardware. If confirmed, document the existing `gl` launch argument for PlayStation pads.
- **Neighbourhood person data is truncated (pre-existing, unverified impact).** `NBRS` loads only the first 88 person-data entries, although non-version-4 records store 256. On save it writes zeros for everything past 88. If the original game keeps anything there, saving in Simitone wipes it. This is worth checking separately; it isn't related to controller support.
- **New (2026) Steam Controller.** SDL 2.32.2 predates it and has no driver or mapping for it. Through Steam Input it appears as a "Steam Virtual Gamepad", which SDL's mapping database covers, so launching Simitone from Steam is the supported route.
- **Steam Controller (original 2015 model) without Steam.** With Steam closed it presents as a keyboard and mouse ("lizard mode"), not a gamepad, so no code here sees it.
  - SDL 2.32.2, which MonoGame bundles, has a Steam Controller driver, but it is off by default (`SDL_JOYSTICK_HIDAPI_STEAM`). In that version its packet reader only accepts Bluetooth LE reports, so it wouldn't help over the USB dongle or a cable.
  - Practical routes: add Simitone to Steam as a non-Steam game (Steam Input presents a standard gamepad), or run a standalone driver that creates a virtual gamepad.
- **Pie menu anchor.** The menu is currently placed at the mouse. With E5 it will be placed at the target's projected screen position, which has to be clamped to the screen bounds.
- **Text input.** Resolved in Phase 6: an on-screen keyboard (§15). The cheat box still opens only from the keyboard shortcut.

---

## 10. Phase 1 — what was built

The engine commit is `b4a41d2` on `simitone/controller-phase1` in `Sleepsong/FreeSO`, on top of upstream `alex-forked` (`7e76c19`). The Simitone submodule now pins it. No Simitone code changes were needed for this phase.

### New engine pieces (`tso.common/Rendering/Framework/IO/`)

| File | Role |
|---|---|
| `GamePadInput.cs` | `UpdateState.Pad`. Polls the first connected pad (rescanning once a second while none is connected) and applies a rescaled radial stick dead zone plus a trigger dead zone. Exposes `IsDown` / `Pressed` / `Released` / `PressedRepeat` per `PadAction` (0.4 s repeat delay, 0.1 s interval). `NavPressedRepeat` accepts either the D-pad or the left stick. `Active` is set only when input *starts*, so a drifting stick can't steal the pointer from the mouse. |
| `PadBindings.cs` | Default action → button map, an optional confirm/cancel swap, and overrides from `gamepad.ini`. |
| `GamePadSettings.cs` | Static options read by the input loop, filled in from `config.ini` in `GameFacade.Init`. |
| `PointerDeviceTracker.cs` | Sets `UpdateState.LastInputDevice`: whichever of mouse or pad was used last wins, and the mouse wins ties. It ignores the OS echoing a cursor warp, jitter up to 3 px, and the zeroed mouse state while the window is unfocused. |
| `VirtualCursor.cs` | The fallback pointer, active while the pad is the active device. |

`GameScreen.Update` polls the pad every frame. In mouse mode it also runs the tracker and the virtual cursor before the existing Alt+click emulation. Touch mode polls the pad and tracks the device, but never uses the virtual cursor.

### Virtual cursor controls (fallback only)

| Pad | Acts as |
|---|---|
| Left stick | Moves the cursor. Speed grows with the square of deflection, about 1000 px/s at full tilt in a 1080 px tall window. |
| A / Cross | Left mouse button |
| X / Square | Right mouse button. Hold it and move the stick to pan the lot (the existing right-drag scroll). |
| Y / Triangle | Middle mouse button. Click it to rotate the camera (existing behaviour). |
| RT / LT | Scroll wheel up / down, which zooms the lot |
| B / Circle | Holds Escape, which cancels held objects and build tools |

The OS cursor is warped to the virtual position, so the game's normal cursors appear where the pad is pointing. Synthetic wheel scrolling is kept as an offset on top of the real wheel value, so switching devices never makes the zoom jump.

### Settings (`config.ini`)

| Key | Default | Meaning |
|---|---|---|
| `GamePadEnabled` | `true` | Ignore all pads when false |
| `GamePadVirtualCursor` | `true` | Use the stick-driven fallback cursor |
| `GamePadDeadZone` | `0.2` | Radial stick dead zone, clamped to 0–0.9 |
| `GamePadCursorSpeed` | `1` | Virtual cursor speed multiplier, clamped to 0.1–5 |
| `GamePadSwapConfirmCancel` | `false` | Confirm on B and Cancel on A (Nintendo-style) |

Optional `gamepad.ini` in the user directory, one binding per line:

```ini
# Action names: Confirm Cancel FaceLeft FaceTop ShoulderLeft ShoulderRight TriggerLeft TriggerRight
#               Up Down Left Right Start Select LeftStickClick RightStickClick
# Button names: MonoGame Buttons enum (A, B, X, Y, Back, Start, LeftShoulder, DPadUp, ...)
Confirm = A
Cancel = B, Back
```

A line that doesn't parse leaves that action on its default binding, and the problem is written to the console.

### Verification

- The full Simitone desktop build succeeds with the patch applied (0 errors, no new warnings in the touched files).
- A headless harness (139 checks) covers:
  - binding parsing and swap
  - press/release/repeat edges
  - dead-zone maths
  - focus loss and disconnect
  - device arbitration, including warp echo, jitter, failed warps and refocus
  - cursor speed, clamping, button and Escape mapping, and wheel accumulation
- **Not yet tested with a real controller or in the running game.** This container has no display, pad or Sims install.

Things to check on hardware:
1. Xbox and PlayStation pads on Linux (DesktopGL/SDL2) and on Windows (default DirectX backend, plus the `gl` launch argument).
2. Whether cursor warping works under your desktop session (native Wayland may ignore it; the game would still work, but the visible cursor would stay put).
3. Whether the cursor speed and dead zone feel right.

### Notes for Phase 2

- Once the lot gets native pad handling, the virtual cursor must stand down inside the lot. Otherwise A would both open the pie menu natively and click through the fake mouse. Plan: a per-frame "pad captured" flag that consumers set, which `GameScreen` checks before applying the virtual cursor.
- S8 comes first in Phase 2: `TS1GameScreen.Desktop` and other `FSOEnvironment.SoftwareKeyboard` checks need splitting into "mobile layout" and "touch input".

---

## 11. Phase 2 — what was built

The engine commit is `9103581` on `simitone/controller-phase2` in `Sleepsong/FreeSO`, on top of Phase 1. The rest is in Simitone.

### How live mode plays with a pad

The lot takes over the pad in live mode whenever no modal dialog or text field has focus. The virtual cursor stands down and the OS cursor is hidden. When a dialog opens, the pad goes back to the virtual cursor, which starts at the centre of the screen, until Phase 3 adds native dialog navigation.

| Input | Live mode |
|---|---|
| Left stick | Move the selector. It snaps to objects and people within about 0.6 tiles of their footprint. Pushing it into the screen edge scrolls the camera and stops following the Sim. |
| A | Interaction menu for the selected object, or "go here" on empty floor. The press that switches from mouse to pad only reveals the selector. |
| B | Back / close |
| Y | Focus the action queue: ←/→ choose, A cancels the action, B or Y leave |
| LB / RB | Previous / next household Sim; the camera follows the new Sim |
| LT / RT | Open/close the needs panel / next panel tab (needs, job, personality, relationships, inventory) |
| Right stick | ↑↓ zoom; ←→ rotate (continuous in 3D, one step per flick in 2D) |
| D-pad ← / → | Game speed down / up |
| D-pad ↑ / ↓ | Floor up / down |
| L3 | Walls: down → cutaway → up → roofs |
| R3 | Re-centre on the Sim and follow it |
| Start | Pause / resume (resumes at the previous speed) |
| Select | Next mode: Live → Buy → Build → Options |

**Pie menu:** the stick points at a slot, the D-pad and LB/RB step through all items including Back, A chooses, and B goes back or closes. A only works after the press that opened the menu has been released.

**Selector marker:** a ring on the floor under the target, with a bobbing marker above it (since Phase 7, the Simitone note; see §16).
- **Green:** the target has interactions.
- **Red:** it has none right now.
- **White:** empty floor.

Hovering a person shows their name.

**Cutaway:** follows the selector while you're moving it, then goes back to the active Sim three seconds after you stop.

**Buy, Build and Options modes:** the left stick, A/B/X/Y and the triggers stay on the virtual cursor (native build/buy is Phase 5). The camera stick, Select, Start, D-pad, L3 and R3 still work natively, because the virtual cursor doesn't use them.

### Changes from the original mapping (§6)

- **Right stick zooms and rotates** instead of panning. Rotation had no binding, and pushing the selector to the screen edge already pans.
- **Triggers drive the needs panel** instead of zoom. Seeing needs is required to play a day, and the right stick now zooms.

### Layout (S8)

`UILayout` in `config.ini`:

| Value | Layout |
|---|---|
| `auto` (default) | Mobile layout on touch screens, or when the pad was the device in use as the lot loaded; desktop otherwise |
| `desktop` | Always the desktop layout |
| `mobile` | Always the mobile layout |

The choice is made when each lot loads. Switching devices mid-lot doesn't change the layout until the next lot.

### Code

| Where | What |
|---|---|
| Engine `UpdateState` / `GameScreen` | `PadCaptured` / `PadWasCaptured`. The virtual cursor stands down the frame after a screen captures the pad, and restarts at the centre when capture ends. |
| Engine `UILayer` | `HasModalDialog` |
| Engine `World` | `ProjectToScreen` / `ProjectTileToScreen` (tile → screen, the inverse of `EstTileAtPosWithScroll`, in both 2D and 3D) |
| Engine `GlobalSettings` | `UILayout` |
| `UILotControlPadHelper` (new) | Everything in the live-mode table above, plus the selector marker |
| `PadMath`, `PadGraphics` (new) | Selector geometry (screen ↔ tile movement, safe-area overflow, footprint distance, menu clamping) and the procedurally drawn ring and marker |
| `UILotControl` | Hosts the helper. `ShowPieMenu` now picks with the mouse, then calls the new `OpenPieMenu(target, tile, position)` that the pad also uses. Mouse hover, edge scroll and mouse-driven cutaway are skipped while the pad drives the lot. |
| `UIPieMenu` | Pad navigation |
| `UIInteractionQueue` | Pad focus and a highlight ring |
| `UISimitoneFrontend` | `CycleSim` (the Space key now uses it), `SetModeDirect`/`CycleMode`, `SetWallsMode` |
| `UILotControlTouchHelper` | `NudgeZoom`, so pad zoom shares the wheel's zoom-snapping hold-off |
| `TS1GameScreen` | Layout choice per lot; hides the OS cursor while the pad drives the lot |

### Verification

- The full desktop build succeeds with no warnings in new code.
- The headless harness now has 161 checks. The new ones cover:
  - screen→tile movement solving
  - safe-area overflow
  - footprint distance
  - menu clamping
  - stick → pie-slot mapping (checked against the pie's layout convention)
  - layout selection
- **Not tested in the running game.** The selector, camera, pie and queue behaviour all need checking on hardware.

### To check on hardware

1. The selector moves at an even speed in every direction in 2D and 3D, at every rotation and zoom.
2. The marker sits on objects and on items on counters. If it's offset by a constant amount, the likely cause is the DPI or smooth-zoom conversion in `ToUI`.
3. Snapping picks sensible targets in cluttered rooms (radius and tie-breaking are constants at the top of `UILotControlPadHelper`).
4. 3D rotation direction feels natural; flip the sign of `RotateRate3D` if not.
5. The camera follow and the selector's edge push don't fight each other.
6. Pie navigation with 2, 4, 8 and more than 8 items, and inside sub-categories.
7. Dialogs that appear mid-play are reachable with the virtual cursor, and the lot regains the pad when they close.
8. The `auto` layout picks the mobile layout when a lot is loaded with the pad.

### Hardware results so far

- **2D, Linux, new Steam Controller through Steam Input:** the selector, pie menu, cursor hand-off and buttons all work.
- **3D:** works, but the selector locked onto objects too easily.
  - Cause: the snap radius was a fixed 0.6 tiles, which covers far more of the screen up close.
  - Fix: snapping now uses a fixed on-screen distance (36 UI px), converted to tiles at the selector and clamped to 0.2–0.6 tiles. The tie-break biases scale with it.

### Fixes after the first hardware test

- **Exception popups when the selector sat on a Sim.** The selector re-checked its target's interactions twice a second. For a Sim, that runs every social interaction's check tree, and Makin' Magic's "Magic - Attraction" expression (reached from "Kiss Cheek TEST") read an avatar slot that doesn't exist. `VMAvatar.GetSlot` had no bounds check, so each re-check threw and raised another "SimAntics Exception" dialog. Two fixes:
  - Engine `f6741e2`: out-of-range avatar slots now read as empty, as they already did for objects.
  - The selector checks a target's interactions once, when it lands on it (as mouse hover does).
  - Side effect: the marker colour can be stale until the selector moves to another target or the active Sim changes.
- **More exception popups (second test).**
  - **"MAGIC - Ghost Social Test"** (from social check trees). Not controller-specific: the log showed it firing 50+ times in 18 seconds from Mortimer's own free-will checks. Sims' person-data arrays could be shorter than the 101 entries `GetPersonData` accepts, and this check reads entry 84. There are two sources of short arrays:
    - neighbourhood records (88 entries);
    - houses saved by the original game (80 entries before Superstar).

    Engine `e1e26f5` covered only the first. Engine `e073db4` pads every avatar's person data to 101 on load and when merging the neighbourhood record.
  - **Object threads failing in `VMThread.Pop`** (sofas, benches, paintings, a wall phone). The log confirmed the cause: frames restored from the saved lot show `<no routine>`, because `VMStackFrame.Load` couldn't find their routine ID in the installed content.
    - Engine `ba9bbee` detects these frames at load and resets the object on its first tick. That's the same outcome the exception handler produced, without the exception.
    - It also writes a note with the missing routine ID and owner GUID.
    - Second log: 11 objects had missing routines (mostly `0x1000`, the first private routine ID). Tiled Counters resumed `main` at an instruction past its end. Both are consistent with the saved stacks coming from older versions of those objects than the ones installed.
    - Engine `ab9284b` also treats positions past a routine's end as unresumable.
    - It also logs, per object type, when the house file's `OBJT` main/init tree versions differ from the installed main/init BHAV versions. If those mismatches line up with the broken objects, the converter should reset such objects, as the original game presumably does. That is unconfirmed until the next log.
- **Error log.** Engine `f53a6ab` adds `VMExceptionLog`. Simitone writes every SimAntics exception to `simantics-errors.log` next to `config.ini`, including when dialogs are suppressed with `-nosimantics-exc`. Each report has the full .NET stack trace and every VM frame (routine name and ID, instruction, caller, callee, owner GUID). Repeats are counted after three full reports, and the file rotates at 8 MB.

### Notes for Phase 3

- Native dialog navigation (E4 focus navigator) is now the main gap. Every VM dialog currently falls back to the virtual cursor.
- The needs panel opens and changes tabs natively, but its contents (relationship entries, job buttons, the inventory) still need focus navigation.

---

## 12. Phase 3 — what was built

The engine commit is `931ccd7` on `simitone/controller-phase3` in `Sleepsong/FreeSO`, on top of Phase 2.

### Focus navigation (engine, `FSO.UI/Framework/UIFocusNavigator.cs`)

- **What can take focus:** within a scope element, the visible, enabled buttons that have a click handler (display-only buttons such as portraits are skipped), plus anything implementing `IPadNavigable`.
- **`IPadNavigable` elements** (lists, the outfit carousel) can use a direction themselves, e.g. moving the selection within a list, and hand focus on at their ends.
- **Moving focus:**
  - D-pad or left stick moves to the nearest element in that direction.
  - Elements in the same row or column always win over diagonal ones.
  - Holding a direction repeats.
- **Buttons:**
  - A clicks (`UIButton.PerformClick`).
  - B calls the scope's `IPadScope.PadCancel`.
  - A new scope ignores A/B until both have been released, so the press that opened a dialog can't also answer it.
- **Outline:** a pulsing outline marks the focused element while the pad is the device in use.

### Dialogs

- **Navigation:** `UILayer` navigates the top modal dialog whenever a pad is connected, and captures the pad so the virtual cursor stays away. This covers every modal dialog in the game, including outside lots.
  - A dialog with nothing focusable keeps the pad captured for its first second, while its opening animation runs.
  - After that, it's left to the virtual cursor.

| Dialog | Starts on | B |
|---|---|---|
| Messages, Yes/No, Yes/No/Cancel (`UIMobileAlert`) | Yes or OK | Cancel, else No. On an OK-only message, OK. Never submits a text entry. |
| Phone book (`UICallNeighborAlert`) | Family list. ↑↓ choose; → moves to the neighbour list, then the Call/Cancel buttons | Cancel |
| Outfit / pet picker (`UISelectSkinAlert`) | Carousel. ←→ turn it, A picks the body in front | Cancel |

**Text entry:** the text box now gets keyboard focus as soon as its dialog opens, so typing works without clicking it first, including from Steam's on-screen keyboard (Steam + X). Focus is released when the dialog closes. The lot helper also clears focus left on any element that is no longer on screen. That fixes an existing bug where, after typing in a dialog, the 1/2/3/P speed hotkeys stopped working.

### Panels

- **Needs panel, LT cycles:**
  1. closed → open (display only, the selector keeps the pad);
  2. → focused: the D-pad/stick move between that tab's buttons and lists, A activates, B unfocuses and leaves the panel open;
  3. → closed.
  - Tabs with nothing to select (motives, personality, job) skip the focused step.
  - RT still changes tab. While focused, the D-pad's floor/speed bindings and Y (queue) are off, and the selector and its marker are idle.
- **Options mode** (Save, Neighbourhood, Quit, free-will toggle): navigated directly as soon as Select reaches it. B returns to live mode. The quit and leave confirmations are modal dialogs, so they're navigated too.
- **Buy and build:** still on the virtual cursor until Phase 5.

### Lists

`UITouchScroll` (the phone book lists, relationships, inventory) implements `IPadNavigable`. Moving along the list selects the next or previous item and scrolls to it. At either end, or across the list, focus moves on.

### Verification

- The full desktop build succeeds.
- The headless harness now has 190 checks. The new ones cover neighbour picking (grids, edges, same row beating a closer diagonal, the outfit picker layout), person-data padding and log notes.
- **Not yet tested in the running game.**

### Hardware results (first test)

| Check | Result |
|---|---|
| Saving from Options, then B back to live | Works |
| The selector after dialogs close | Works |
| LT cycle on the needs panel | Works: the third press closes it |
| Carousel | Right turns it clockwise seen from above: the front body moves left and the one on its right comes forward. That's the usual list convention, so it stays unless it feels wrong. |
| Phone book Cancel | Went on to "Do you want to invite Unknown or just talk?". The same happened with a mouse click. |
| Dialog focus outline | Not visible in the screenshot sent; still to confirm |

**Phone book Cancel:** a pre-existing engine bug. `VMDialogPrivateStrings` ignored the phone book's response code and parsed Cancel's "-1" as a neighbour ID. Engine `cad8144` makes code 0 take the false branch, as the clothes and pet pickers already did.

### To check on hardware

1. A message, a Yes/No and a three-button dialog: starting focus, left/right between buttons, A, B.
2. The phone book and the outfit/pet picker. Check which way the carousel turns for left/right.
3. A naming dialog (baby or pet): the text box takes typing straight away, and B doesn't submit.
4. Needs panel: LT three times on the relationships and inventory tabs; RT while focused.
5. Options: Select into Options, Save, then B back to live.
6. After any dialog closes, the selector works again without touching the mouse.

---

## 13. Phase 4 — what was built

Engine commits `b28765a` and `6167223` on `simitone/controller-phase4`. Simitone turns it on with `vm.AllowDirectControl = true` in `TS1GameScreen`.

### How it plays

- **X** in live mode with the 3D camera switches to direct control. A short hint ("Direct control" / "Cursor mode") shows near the top of the screen. In 2D, X shows "Direct control needs the 3D camera." and plays the error sound.
- **Left stick:** walks the Sim relative to the camera (stick up = away from the camera), through `World.Transform` like FreeSO's keyboard direct control. Past 0.9 deflection the Sim runs, and keeps running until it drops below 0.75. Letting go stops them; they keep facing the way they last walked.
- **Camera:** follows the Sim's position with light smoothing, and follows them up and down stairs. The right stick still zooms and rotates.
- **A:** opens the menu of what the Sim is facing: in their room, within 1.25 tiles and 35° of their facing, the best by distance plus angle. The marker shows what that is. With nothing there, A does nothing (there's no "go here" in direct control). Choosing an interaction queues it as usual; the Sim walks over and does it, then direct control resumes.
- **Everything else** (LB/RB, Y, LT/RT, D-pad, Start, Back) works as in cursor mode. Direct control follows the active Sim: switching Sims hands it to the new one and releases the old one.
- It's switched off (and the Sim stops) in buy/build/options mode and when the camera goes 2D, and comes back when they return, until X turns it off. Leaving it in live mode puts the selector on the Sim.

### Engine (`tso.simantics`)

| Change | Why |
|---|---|
| `VM.AllowDirectControl` | Explicit opt-in. Both direct control commands' `Verify` accept it as well as TSO's `aprilfools` tuning, so TSO is unchanged. |
| `VMDirectControlFrame.TS1Routine` | A routine built in code: one Look Towards in `FSODirectControl` mode, which pushes the frame. `EnsureDirectControlAction` uses it under TS1 instead of looking up `0x2329`. |
| `VMAvatar.DirectControlEnabled` | The enable flag under TS1. It's a runtime field, so it never reaches save data (TSO still uses `PersonData[54]`). |
| `VMThread.Load` | Drops saved direct control actions under TS1, and restarts the avatar if one was running. |
| `VMNetChangeControlCmd` | Ends direct control on the Sim being let go. Its PersistID becomes 0, so a toggle sent afterwards could never reach it. |
| `VMNetDirectControlToggleCommand.SetEnabled` | The toggle's body, shared with the control change. |

### Changes from the plan (§4, E6)

- **Routine in code, not a PIFF.** The PIFF route was chosen so saved actions would resolve. Handling saves on load is simpler and needs no content patch: a saved TS1 direct control action is dropped by its flag (`0x2329` might exist in TS1 content as something unrelated, so resolving isn't enough), and if it was running, the Sim's stack is cleared and restarted. The pad helper turns direct control back on if the player still has it selected.
- **No cancel before save.** The load-side cleanup covers it, including saves made by other code paths.
- **`TryMove` quirks left alone.** Both are harmless. The missing `else` gives a stopped Sim one zero-length step, and the `2500` threshold only adds collision substeps. Changing the step count would change movement results, and a TSO client built from this fork would then disagree with upstream servers.
- **Target selection** is a 35° cone out to 1.25 tiles rather than the plan's 2 tiles, which would reach past the adjacent tile. The first version (nearest to a point in front) was replaced after the hardware test; see below.

### Autonomy

Not suppressed explicitly. `EvaluateQueuePriorities` re-queues the direct control action every tick, so the controlled Sim's queue is never empty. TS1's free will runs from the idle loop when the queue is empty, so it shouldn't fire while direct control is on. Interactions pushed onto the Sim by other objects or Sims (the phone, a visitor) still interrupt it, which is the game's normal behaviour. This is reasoning, not a test result: check it on hardware.

### Known limits

- Movement is bounded by the current room plus portals (doors, stairs). That's fine in houses but may feel restrictive on large outdoor community lots.
- Direct control only runs while the Sim's posture is standing. After sitting or lying down, it resumes once they stand.

### Verification

- The full desktop build succeeds.
- The headless harness now has 226 checks. The new ones cover direction conversion (each direction, wrapping, round trips through the frame's own sin/cos), the facing vector, the TS1 stand-in routine, the facing cone (beside, behind, diagonal, out of reach, a long counter alongside), the camera follow and run/walk hysteresis.

### To check on hardware

1. 3D camera, live mode: X, then walk. Stick up should go away from the camera. Rotate the camera with the right stick while walking.
2. Full tilt runs; partial tilt walks; letting go stops.
3. Walk through a door and up and down stairs.
4. Walk up to the fridge or another Sim: the marker should show on it, A opens its menu, and after the interaction the stick works again. A facing nothing does nothing.
5. LB/RB in direct control: the new Sim walks with the stick, and the old one goes back to normal (not frozen in place).
6. Stand still for a few in-game hours with free will on: does the controlled Sim stay put?
7. X again: cursor mode, with the selector at the Sim. Switch to 2D while in direct control: the cursor appears at the Sim. X in 2D: the hint and error sound. Back to 3D: direct control resumes.
8. Buy mode while in direct control: the Sim stops. Back to live: it resumes.
9. Save while in direct control, quit, load: no new entries in `simantics-errors.log`, the Sim isn't stuck, and X still works.

### Hardware results (first test)

| Check | Result |
|---|---|
| Walking relative to the camera | Works, but the camera shook a lot while running |
| Run / walk / stop | Works |
| Doors and stairs | Works |
| A on what's in front | Works, but the target was often to the Sim's side or through a wall |
| Switching Sims with LB/RB | Works |
| Free will while in direct control | The Sim stays put |
| X / 2D / 3D switching, buy mode | Works |
| Save and load in direct control | Works |

### Fixes after the first hardware test

- **Camera shake while running.** Diagnosed from the code, not measured. The likely cause is that the anchored camera (`World.CenterTo`) re-centres on the pelvis bone every frame, and the run cycle sways it. In direct control the pad helper now moves the camera itself, following the Sim's position with exponential smoothing (15/s; it jumps instead beyond 3 tiles), and sets the floor to the Sim's. Running also has hysteresis now (on at 0.9, off below 0.75). This rules out a second possible cause: acceleration ignores stick intensity, so a stick resting near a single threshold would flip between walk and run speeds, and the walk/run animation blend with them.
- **Target to the side or through walls.** The old search took the nearest interactable within 0.9 tiles of a point 0.9 tiles ahead. That reaches about 0.9 tiles sideways, and it ignored walls. Now a target has to be in the Sim's room (walls and fences bound rooms; each tile of a multi-tile object is checked on its own) and within 1.25 tiles and 35° of their facing. The score is distance plus 1 per radian off centre. Along with the point nearest the Sim, the search tries the points nearest two spots straight ahead, so a long counter counts by its part ahead of the Sim and not the part beside them.

### Retest results

The camera is steady when running, and targeting picks what the Sim faces.

### To check on retest (done)

1. Run in a straight line and in circles, zoomed in: the camera should be steady.
2. A stick held at full tilt keeps running, without flickering between walking and running.
3. Walk alongside a counter or a row of objects: nothing beside the Sim gets the marker. Turn to face one and it does.
4. Stand next to a wall with an object on the other side: it isn't picked.
5. Face a corner between two objects: the one more squarely in front wins, and the marker doesn't flicker between them.

---

## 14. Phase 5 — what was built

Buy and build mode with the pad. Everything is in Simitone; the engine didn't need changes. Before this, the pad drove buy and build through the virtual cursor (a mouse pointer moved by the stick).

### How it plays

The pad is either on the **lot** or in the **catalog**. Entering buy or build mode puts it on the lot.

**On the lot:** a cursor sits at the screen centre (a white ring), and the left stick scrolls the lot under it at a screen-relative speed.

- **A** is the mouse button. On an object, it picks it up. While holding something, it places it. With a wall, floor or wallpaper tool, press A at one end, scroll, and let go at the other end.
- **LB / RB** rotate what you hold a quarter turn.
- **Y** sells what you hold. A new catalog item is just put back. With nothing held, a hint says to pick the object up first. Selling an object you can't see first seemed too easy to do by mistake.
- **B** puts back what you hold (a moved object returns to where it was) or puts the tool away, then goes to the catalog. With nothing held, B also goes to the catalog.
- **X** with nothing held is the eyedropper: the object or floor under the cursor is picked from the catalog, switching between buy and build if needed.
- **With a build tool out**, hold **X** for its Shift form (walls: a room; floors and wallpaper: fill) and **Y** for its Ctrl form (erase or delete).
- **D-pad ↑/↓** change floors. The right stick, L3, R3, Select and Start work as in live mode.

**In the catalog** (LT to enter or leave, RT for the next category):

- The D-pad or stick move between the subcategory icons. A opens one. B goes back from items to subcategories, then out to the lot.
- In a list of items, left and right move a highlight **without buying anything**, and A picks the highlighted item. The pad then goes to the lot to place it. The A press that picked the item doesn't also place it; A has to be released first.
- After a new item is placed, the pad returns to the catalog on the same item, so A picks another of the same.

### Code

| Where | What |
|---|---|
| `UILotControl.PadCursor`, `PadModifiers` | Set by the pad helper while it drives buy/build. Build tools take their position and Shift/Ctrl from them instead of the mouse and keyboard. |
| `UILotControl.ReleaseCustomControl` | Puts the tool away, as Escape does. |
| `UIObjectHolder.PointerPosition` | The holder's pick-up GPU pick, placement position, eyedropper and error tooltips use the pad cursor when it's set. |
| `UITouchScroll.PadHighlightOnly` | A highlight that moves without selecting, and A selects. Used by the catalog, where selecting creates the object. The focus outline is drawn around the highlighted item. |
| `UILotControlPadHelper.UpdateBuild` | Lot/catalog focus, the centre cursor, scrolling, the buttons above. LT/RT come from `HandlePanelTriggers`, shared with live mode. |
| `UISimitoneFrontend.SwitchMode` | With the pad, switches straight to the mode (the eyedropper's buy↔build switch). The pad can't drive the mode-selection animation the mouse path uses. |

### Choices and departures from the plan (§5, S4)

- **LT never closes the panel in buy or build mode.** Closing it removes the catalog and drops whatever you hold, which is why buy/build mode hides the close button. LT only moves the pad in and out of the catalog.
- **Back to the catalog after placing.** The alternative was to keep holding another of the same item, like Shift-click. That turns a double press of A into an accidental second purchase.
- **Y doesn't sell what's under the cursor.** You pick it up first (A), so you see what you're selling.
- **The catalog fades** between subcategories and items, and may have nothing focusable for a moment. Catalog focus survives up to a second of that instead of dropping out.

### Known limits

- The catalog search field needs a keyboard (no on-screen keyboard yet).
- There's no hover highlight before you pick up: aim the centre ring at the object. The pick is the same GPU pick the mouse uses.

### Verification

- The full desktop build succeeds.
- The headless harness now has 236 checks. The new ones cover the catalog highlight: steps, both ends, clamping after a search shrinks the list, and the highlighted item's rectangle, including scrolled, clipped, out-of-view and vertical cases.
- **Not yet tested in the running game.**

### Hardware results

Everything on the list above works.

### To check on hardware (done)

1. Select into buy mode. The ring sits at the centre and the left stick scrolls the lot.
2. LT into the catalog, RT through categories, choose a subcategory, move along the items (nothing should be bought while moving), and A on one. It should follow the ring without being placed by the same press.
3. LB/RB rotate it. A places it, and the pad returns to the catalog on that item. A again picks another.
4. B while holding a new item: it's put back and you're in the catalog. B in items: back to subcategories. B again: back on the lot.
5. On the lot, A on a placed object picks it up. Move it, place it with A, or put it back with B. Pick one up and sell it with Y.
6. Build mode: walls (A, scroll, release), and a room by holding X. Delete walls by holding Y. Floors and wallpaper the same way, including fill (X) and erase (Y).
7. X on an object in buy mode, and on a floor (it should switch to build mode and pick that floor).
8. Switch to the mouse in the middle of any of this. The mouse should work normally, and the pad should pick up again when you press something.

---

## 15. Phase 6 — what was built

Engine commit `6b30b0a` on `simitone/controller-phase6`. The rest is in Simitone. The neighbourhood, CAS and every text box now work with the pad, and button prompts use the pad's own naming.

### On-screen keyboard

It appears whenever a text box has keyboard focus and the pad is the device in use: in dialogs (naming a baby or pet), CAS names and bio, and catalog search. It sits in a new overlay slot in the UI layer, so it covers every screen and dialog and gets the pad first.

| Input | Action |
|---|---|
| D-pad / left stick | Move between keys. Rows wrap, and up/down remember the column, so passing through Space comes back to the same letter. |
| A | Press the key |
| B | Delete. On an empty box, a fresh press closes the keyboard. |
| Y | Space |
| X | Shift (one key; it starts on for an empty box, to capitalise names) |
| Start, or the Done key | Close |

A preview line shows what's been typed, since the box itself may be under the keyboard. Closing releases the box's focus. A physical keyboard keeps working throughout, and using the mouse hides the on-screen one.

To reach a text box: the focus navigator now includes editable text boxes, and A on one gives it focus. In a naming dialog the box already has focus when the dialog opens, so the keyboard is up straight away. In the buy/build catalog, **Y** opens the search box.

### Neighbourhood

- The D-pad/stick move between houses on the map and the neighbourhood buttons at the edges (CAS, back, other neighbourhoods). The focused house shows the same opened-up picture as mouse hover.
- **A** opens the house panel; focus starts on Enter Lot (or More when Enter Lot isn't available).
- **B** in the house panel goes from the More options back, then closes the panel. Focus returns to that house.
- Focus starts on the last house selected, otherwise the one nearest the middle of the screen.

### CAS

Each mode navigates only its own panel plus Back and Accept. The other modes' panels stay on screen, slid away or faded, so they're excluded with `IPadScopeRoots`.

- **Family select:** the family list (moving selects), New and Delete. **Start** moves the selected family in.
- **Family edit:** the last-name box, the members and "+". A on a member shows Edit/Delete above it. **Start** accepts the family.
- **Sim edit:** the tabs, first-name box, gender/age/skin and Random buttons, the personality bars (left/right take a point off or add one, within the points left), the bio box, and the head and body carousels. On a carousel, left/right turn it one place with a short glide. LB/RB turn the head carousel and LT/RT the body one from anywhere. **Start** accepts the Sim.
- **B** is the Back button in every mode, including the confirmation it asks for when leaving family edit.
- A prompt line along the bottom names the buttons. Back/Accept stay reachable with the D-pad.

### Settings and prompts (S7)

| Key | Default | Meaning |
|---|---|---|
| `GamePadGlyphs` | `auto` | Button names in prompts: `auto` (from the pad's name), `xbox` or `playstation` |
| `GamePadInvertZoom` | `false` | Right stick up zooms out instead of in |
| `GamePadDirectControl` | `false` | Live mode starts in direct control (3D only) instead of cursor mode |

Prompts name the button each action is bound to, so `GamePadSwapConfirmCancel` and `gamepad.ini` rebinding read correctly. PlayStation naming (Cross, Circle, L1, Options...) is chosen when the pad's name looks like a Sony pad, including Sony's generic "Wireless Controller". Everything else, including Steam Input's virtual pad, gets Xbox names. Prompts are text only; there is no button artwork.

Not done from S7: a settings screen (these are `config.ini` keys) and a separate cursor/pan speed for the lot (the stick speeds are constants).

### Engine changes

| Change | Why |
|---|---|
| `UITextEdit.PadType` / `PadBackspace` / `IsReadOnly` | Typing that respects `MaxChars`, validation and `OnChange` (catalog search filters live). Injecting characters into the text input would have worked for letters but not Backspace, which `ApplyKeyboardInput` only takes from a physically pressed key. |
| `UIFocusNavigator`: text boxes; `IPadScopeRoots` | Text boxes are reachable. Scopes can name exactly what to navigate. |
| `UILayer.Overlay`, `UpdateState.PadTextEntry` | The keyboard must be updated before screens and dialogs (to take the pad) but drawn after them; one tree can't do both. Dialog navigation stands down while the flag is set. |
| `PadGlyphs`, `GamePadInput.DisplayName`, settings | Prompts in the pad's naming. |

### Verification

- The full desktop build succeeds.
- The headless harness now has 289 checks. The new ones cover keyboard movement (wrapping, wide keys, the remembered column, ties), shifted characters, that every row tiles ten columns, button naming (auto-detection, overrides, swapped bindings) and the carousel glide (lands exactly, never overshoots, short).
- `UITextEdit` can't be constructed without graphics, so `PadType`/`PadBackspace` have no harness coverage; they were checked by reading them against `ApplyKeyboardInput`.
- **Not yet tested in the running game.**

### Hardware results

Everything worked except the focus outline on the neighbourhood's round buttons, which sat down and to the right of them, and those buttons themselves were out of place (fixed in Phase 7, §16).

### To check on hardware (done)

1. Start up: the neighbourhood map takes the pad without the virtual cursor. Move between houses (they open up as focused), A opens a house, B closes it, and More/Back work.
2. Move to the CAS button at the left edge and press A.
3. Create a family: last name (A on the box, type, Start), "+" for a new Sim. Name them, change gender/age/skin, turn the head and body carousels (left/right on them, and LB/RB and LT/RT), set personality points and type a bio. Start accepts.
4. Edit and delete a member from the family edit screen. B on the family edit screen asks before discarding the family.
5. Accept the family, pick it in family select, move it into a house, and play.
6. In buy mode, LT into the catalog and Y to search, then type a few letters. The list should filter as you type.
7. Name a baby or pet if you can: the keyboard should be up as the dialog opens. Done, then OK with the pad.
8. With a PlayStation pad (or `GamePadGlyphs = playstation`), the prompts say Cross/Circle.

---

## 16. Phase 7 — UI polish and bug fixes

Engine commits `da2f2f2`, `aaee59d`, `c7ff5b9`, `b4580aa`, `df755b6`, `5e5e3bc`, `2fab2fb` and `f6d4af4` on `simitone/controller-phase7`; the rest is in Simitone. The plan had no Phase 7: it was added after Phase 6 testing, for polish and bug fixes.

### Focus highlight

The yellow box is gone. Focus is now shown as:

- a soft mint frame (Simitone's highlight colour) with rounded corners, breathing gently, around the focused control. It pops out a little when focus arrives.
- the Simitone note bobbing over the focused control (with the frame only, see the sixth round below), where there's room above it and the control isn't a large area (a carousel, a whole list). This is the musical note that forms the "t" of the Simitone logo. It's cut from the logo image when the game loads, keeping only its white top layer, with a dark outline added. If the logo can't be read, a plain drawn quaver is used instead. The lot's target marker uses it too. No Sims artwork or plumbob shapes are used, for copyright reasons.
- round buttons (the neighbourhood buttons, family portraits) grow a little, as they do under the mouse. The focused house on the map opens up, as before.

The engine draws it through a new hook (`UIFocusNavigator.FocusRenderer`), so it applies everywhere: dialogs, panels, the catalog, the neighbourhood and CAS. The on-screen keyboard uses the same green for its focused key.

### Button prompts

A prompt bar shows the buttons that do something right now, with drawn button icons. Xbox naming gets coloured A/B/X/Y discs; PlayStation naming gets the cross, circle, square and triangle symbols. LB/RT and similar are pills, and sticks and the D-pad have their own icons. Like the Phase 6 text prompts, they follow the bindings and the pad's naming. All of it is drawn in code; there's no console artwork.

| Where | Prompts |
|---|---|
| Live, cursor mode | Move; A Interact or Go here; X Direct control (3D); Y Actions; LT Needs; LB/RB Switch Sim |
| Live, direct control | Walk; A Interact (with something in front); X Cursor; Y Actions; LT Needs; LB/RB Switch Sim |
| Pie menu | Point; A Choose; B Back |
| Action queue | Choose; A Cancel action; B Back |
| Needs panel focused | Move; A Select; B Back; RT Next tab |
| Options | Move; A Select; B Back to live |
| Buy/build, nothing held | Move; A Pick up; X Eyedropper; LT Catalog; Back Mode |
| Buy/build, holding an object | Move; A Place; LB/RB Rotate; Y Sell; B Put back |
| Buy/build, using a tool | Move; A Use (hold to drag); X Whole room (hold); Y Erase (hold); B Put away |
| Catalog | Browse; A Open, Buy or Choose; B Back; Y Search; RT Category; LT Lot |
| Neighbourhood | Move; A Visit house. In the house panel: Move; A Select; B Back |
| CAS | A Select; B Back; LB/RB Head and LT/RT Body in Sim edit; Start Move in, Done or Accept when available |
| Any dialog | A Select; B Back |
| On-screen keyboard | Its own row: A Type, B Delete, Y Space, X Shift, Start Done |

In a lot the bar sits at the bottom right, above the main panel when it's open: the clock and queue have the top, and the mode button and money the bottom left. Elsewhere it's at the bottom centre. It's only shown while the pad is the device in use.

### Tooltips

The focused control's name shows in a bubble over it. Icon-only buttons now have names, which mouse hover also shows:

- neighbourhood buttons: Create a family, Back to the neighborhood, Downtown, Vacation Island, Studio Town, Magic Town
- houses on the map: lot name, plus the family living there
- the main panel's tabs: Needs, Job, Personality, Relationships, Inventory; the eight buy categories; the three build ones; Options
- mode buttons: Live mode, Buy mode, Build mode, Options
- CAS: Back, Accept (Move in, Done or Accept by mode), Create a new family, Delete family, the Appearance, Personality and Biography tabs, gender, age, skin and Random Sim, Edit/Delete Sim, and Heads/Bodies on the carousels

### Toasts and keyboard

Lot messages ("Direct control", "Cursor mode", "Pick it up with A, then Y sells it.", "Direct control needs the 3D camera.") show in a rounded panel that fades out, instead of a tooltip. The keyboard has the same rounded Sims-blue panel and rounded keys, and button icons on its hint row.

### Bug fixes

| Bug | Fix |
|---|---|
| The focus outline on round (elastic) buttons sat down and to the right of them | They're drawn and clicked centred on their position, but reported their bounds from the top-left. `UIElasticButton.GetBounds` is now centred. This also fixes family portraits in CAS and the house panel. |
| The neighbourhood's right-hand buttons stayed where the window's first size put them | `UINeighbourhoodSwitcher` re-lays them out on resize. |
| The house panel kept the layout of the size it opened at | It's reopened at the new size after a resize (on the next update, not mid-resize). |
| The map overflowed the sides of screens narrower than 4:3 | It fits whichever dimension is tighter. |
| The house panel drew over its own focus highlight | The screen navigator keeps itself on top of anything added after it. |
| The focus highlight and tooltip showed under the on-screen keyboard | Both stand down while typing. |
| Entering a lot with the pad crashed (found in the first Phase 7 test) | The neighbourhood's prompt function read the navigator through a field that entering the lot sets to null, during that navigator's own update. It now keeps its own reference, and a navigator removed while it updates stops there. |
| Lots near the map's edge were drawn past it, onto the background beside the 4:3 map | Each lot picture is cropped to the map image, as the original 800x600 screen did, and the focus frame is clipped with it. |
| Crashes left no record when the game was started from Steam (they went only to the console) | Unhandled exceptions are also appended to `crash.log` in the user directory, next to `simantics-errors.log`. |

### Verification

- The full desktop build succeeds.
- The harness now has 294 checks; the new ones cover the prompt bar's per-frame model. The drawing itself needs a graphics device, so the generated shapes (glow frame, discs, the four symbols, stick ring, D-pad, and the note cut from the logo) were checked by rendering the same distance maths offline.
- **Not yet tested in the running game.**

### Hardware results (first test)

Better overall. Three follow-ups, all fixed above: use the Simitone note instead of a plumbob, lots drawn off the 4:3 map, and a crash entering a lot.

Still Simitone's own pre-Phase 7 art: the live-mode button's mood meter (`plumb_bg/plus/neg.png`) and the 3D marker over the active Sim (`arrow.fsom`). Neither was added by this work.

### Second round of feedback

| Feedback | Change |
|---|---|
| The selection circle floated above objects (at their height: a mirror's ring hung on the wall) and was small | The ring now lies on the floor and surrounds the target (sizing reworked in the third round, below). It's bolder, with a dark rim so it reads on light floors, a soft glow, and a gentle pulse. A soft beam rises from it to the target (so things on counters and walls connect to their ring), with the Simitone note at the top. Green: has actions. Red: none. White: empty floor (Go here). |
| "There are no actions available" appeared far from the target | That was the pie menu's error tooltip, placed for the mouse. With the pad, A on a target with no actions now says "No actions available." as a message, and the menu's other errors (can't go there, for sale) show the same way. |
| How do you change wall height with the pad? | L3 (press the left stick) cycles walls down, cutaway, up and roofs, as it has since Phase 2. It wasn't in the prompt bar; now it is (live mode, and buy/build with nothing held), and each press names the new setting. |

### Third round: the ring

| Feedback | Change |
|---|---|
| The ring was far too big | It was a circle round the diagonal of the whole object group, and stairs, railings and long counters are huge groups. Now it's an oval hugging the footprint: the whole object only if it's at most 2x2 tiles (a sofa, a bed), otherwise just the tile pointed at. One tile gives a ring just over a tile across, and a Sim a small circle. |
| It looked odd at some camera angles, and huge and blurry close up | It was one ring texture stretched over the ellipse that the circle's two projected radii span: only approximately right in 3D, and its band grew with it. It's now a line through 48 points round the oval, each projected exactly, with a fixed thickness on screen: a glow, a dark rim, then the bright band. Points behind a 3D camera are skipped, using a new engine projection that keeps depth (`World.ProjectTileToScreenWithDepth`, engine `simitone/controller-phase7`). |
| The beam was slanted, and the note sat far above the target | The beam went from the ring's centre to the targeted tile, and the height counted the lot's terrain twice: `VisualPosition.Z` already includes it, and the projection adds it again. The beam now goes straight up from the ring's centre. Its height counts only what the item sits on (a counter or shelf), measured from the bottom of that stack. |

### Fourth round: Simitone's colours

| Feedback | Change |
|---|---|
| Green isn't Simitone's highlight colour, and the dark prompt bar and lot-name bubbles don't match Simitone's light UI | The pad UI now takes its palette from Simitone's own art and styles. Highlights (the focus frame, the note, the keyboard's focused key, the "has actions" floor ring) use the mint of Simitone's green buttons, (97, 240, 213). The prompt bar, tooltip bubbles, toasts and the keyboard are near-white panels with a blue-grey edge, like Simitone's buttons, with navy text (`UIStyle.BtnTxt`, (0, 31, 63)). Button icons on them are navy pills and navy discs (PlayStation symbols in bright colours on the discs), or coloured discs with white letters for Xbox naming. A faint navy shadow under the mint focus frame keeps it visible against light buttons. Red stays for "no actions", white for empty floor. |

### Fifth round: hover looks instead of boxes

| Feedback | Change |
|---|---|
| The focus box isn't needed where a control already has its own mouse-hover highlight (catalog and category buttons, the choices in popups), but is still wanted in character creation, the outfit picker and the neighbourhood | When focus lands on a button with a hover look of its own, the button is shown in that hover state (`ForceState`), as the mouse would show it, and no frame is drawn. Stencil and category buttons recolour, big dialog buttons switch to their hover image, and two-state buttons light up. Anything without one keeps the frame: catalog items, lists, the outfit carousel, text boxes, personality bars, houses, and round buttons whose hover is only a slight grow. The neighbourhood and CAS keep the frame on everything (`UIScreenPadNavigator.AlwaysFrame`). The note still marks the focused control everywhere. A forced hover is released as soon as focus moves or the pad stops drawing it. |

### Sixth round: the house panel and popup buttons

| Feedback | Change |
|---|---|
| The house panel's Enter Lot / More buttons should use their hover look too, not the box | The neighbourhood no longer frames buttons that have a hover look (`AlwaysFrame` off there). Only the house panel's big buttons are affected: the houses and the round buttons have no hover look to speak of and keep the frame. CAS still frames everything. |
| Pad focus on popup buttons doesn't fully look like mouse hover | `ForceState` only switched the button's image; the caption colour follows the mouse state (`CurrentFrame`), so it stayed as normal. The pad now also sets `CurrentFrame` to hover while focused, and puts it back on release (unless the mouse is really over it or holding it down), so image and caption match mouse hover exactly. |
| (found while fixing the above) | Mouse hover on the big buttons turned the caption the engine's default white, which all but vanished on the paler hover image (Enter Lot, OK). `UIBigButton` now sets its hover caption colour: the normal caption colour 35% of the way to Simitone's green, (0, 109, 86) on white buttons and (0, 130, 55) on green ones. Readable, and the same hint of green the icon buttons get on hover. This changes mouse hover too. |
| The note is redundant where the hover look shows focus | The note is drawn only with the frame. The focus tooltip bubble sits closer when there's no note. |

The hover cycle can't be run in the harness: `UIButton`'s static initializer needs a graphics device.

### Seventh round: CAS buttons and the name bubble

| Feedback | Change |
|---|---|
| CAS and the family picker still box buttons that tint under the mouse (Back, Accept, the family buttons, gender, age, skin colour, the tabs) | CAS no longer forces the frame, so those use their hover look like everywhere else. That was the last screen using `AlwaysFrame`, so it's gone. The frame and note remain for what has no hover look: family member portraits, the head and body carousels, name boxes, personality bars, the families list. |
| The name bubble ("Child") covered the button above it | The bubble goes on whichever side of the focused control (above, below, right, left, in that order of preference) covers least of the other controls the pad can move to, each counted by the fraction of it covered, so grazing a carousel matters less than hiding a button. It never covers the prompt bar, or the control itself. It measures from the frame and note when they're drawn. For this the engine now exposes the navigator's targets to the focus renderer (`UIFocusNavigator.Targets`, `UIFocusNavigator.Drawing`, commit `c7ff5b9`). Placement is `PadMath.PlaceLabel`; the harness has 8 new checks for it (306 in all). |

Known limit: Simitone's hover tint for stencil buttons (mint, (128, 255, 192)) is close to its selected tint (green, (0, 255, 128)). With the mouse the cursor shows which is which; with the pad, a focused unselected gender or age button can look selected next to the selected one. A focused selected button shows the hover tint in place of its selected tint while focused, as in the catalog.

### Eighth round: round buttons, lists, the Heads frame, a neighbourhood cursor, Steam Input

| Feedback | Change |
|---|---|
| Create a family, the town buttons and the "+" in Create a Sim have boxes though they grow under the mouse | Round (elastic) buttons now count as having a hover look: the pad puts them in their real hover state, so they grow as under the mouse, with no frame or note. `UIElasticButton.SetPadHover` is gone; the hover state does it. |
| The family picker draws a huge box round the whole list, and keeps it while a family is highlighted | A list's focus rectangle is now its selected item (else the first, which A would pick), not the whole list. The family list shows which family is selected by itself, so once one is, there's no frame at all (`IPadOwnFocus`). |
| The Heads frame is huge | It used the mouse's head region, which reaches from the panel down to the bodies. It's now a band just above the head/body split, sized with the screen height (12% tall, 15% wide), round the centre Sim's head. |
| The prompt says "Visit house" on buttons, community lots and empty lots | On a lot it now says what the house panel it opens is for: "Visit house" (occupied), "Visit lot" (community), "View lot" (empty), or when moving a family in "View house", "View lot", "Move in". On anything else it says "Select". |
| Stepping through every lot takes a long time: a cursor mode would help | X switches the map between browse (jump between lots, as before) and cursor. Cursor hands the map to the virtual cursor: the left stick moves the pointer, A clicks, lots open up under it as with the mouse, and the lot under it is named in the bubble. The house panel is navigated as usual in either mode. Switching to cursor starts the pointer on the focused lot; switching back focuses the lot under the pointer; closing the house panel puts the pointer back on its lot (engine: `UpdateState.PadCursorStart`). The mode lasts for the session. Not offered if the virtual cursor is turned off. |
| With a Steam Controller, Select toggled the camera, the right stick brought up the mouse, the right trigger resized the window | Steam Input's desktop layout sends keyboard and mouse events for the pad (Select is Tab, which toggles first-person; the right stick moves the mouse; triggers click) on top of the pad itself. This wasn't caused by the last update. The engine now ignores the keyboard and mouse while the pad is held (`PadEchoFilter`): any button, a trigger past 0.3, a stick past 0.5, or a stick that moved in the last second, plus 0.25 s after. Meanwhile the keyboard reads as empty, typed text is dropped, the mouse stays put with its buttons up, and mouse movement can't take the pointer from the pad. A stick resting slightly off centre (drift) stops counting after a second, so it can't lock the mouse out. Setting `GamePadIgnoreEchoes` in config.ini (default true). Anything the window manager acts on directly can't be stopped by the game. |

The harness has 17 new checks for the filter and the tracker (323 in all).

**Pad not detected when started from Steam.** Launched by Steam, the game is shown Steam's virtual gamepad instead of the physical controller (Steam sets `SDL_GAMECONTROLLER_IGNORE_DEVICES` and friends), and what that pad sends depends on the Steam layout chosen for the shortcut. To see what's going on without a console, the game now writes `gamepad.log` to the user folder (`~/.local/share/Simitone/` on Linux, next to `crash.log`), rewritten each run (`Utils/PadDiagnostics`). It records the Steam/SDL environment variables, every joystick SDL can see (name, vendor/product, whether SDL has a gamepad mapping for it; MonoGame ignores joysticks without one), when the game's pad connects or disconnects, its first input, and window focus changes (pad input is ignored while unfocused). Checked against the SDL 2.32 MonoGame ships (324 harness checks).

The first log (Bazzite, the new twin-stick Steam Controller, started from Steam) showed the cause: SDL saw two "Steam Controller" pads (28de:1302), both usable, and the game took slot 0 and never got any input from it, so input was going to the other copy. The game used to read the first connected pad and stay on it; after a restart the order changed. Now it reads every connected pad each frame and uses the one being pressed (`GamePadInput.ChoosePad`): the current pad while it's in use, else any pad with a button down or a stick or trigger past halfway, else the current one, else the first connected. A slightly drifting stick on another pad doesn't take over. gamepad.log notes each switch. The harness has 12 new checks (336 in all).

With the pad found, the right stick still moved the system cursor (out of the window, clicking on other monitors) and the triggers clicked, although Simitone's Steam layout has no mouse bindings. That is Steam's Desktop Layout. Shift+Tab and the Steam button do nothing in Simitone, so the Steam overlay isn't running in it. Steam relies on the overlay to know the game is in front; without it Steam keeps the controller on the Desktop Layout. Simitone isn't in the way: the native launcher `execv`s the game (same process), the game doesn't restart itself, SDL 2.32 picks X11 before Wayland, and MonoGame doesn't change the video driver. To find where the overlay fails, gamepad.log now also records whether Steam asked to load it (`LD_PRELOAD`), whether the library is actually loaded in the game (`/proc/self/maps`), the session type and display variables, and SDL's video driver.

The next log: Steam asked to load the overlay and it is loaded in the game, SDL is on X11 (XWayland, in a Wayland session), and only one copy of the controller showed up. Shift+Tab still doesn't open the overlay, which this setup doesn't explain, but the cursor no longer leaves the window: Steam is applying Simitone's layout.

| Feedback | Change |
|---|---|
| With the pad, the camera can keep drifting while a popup is up | The lot scrolls when the hardware cursor is at a window edge (and on right-drag). That was skipped only while the pad helper was active, and the helper stands down while a popup is open, so a cursor parked at an edge (where Steam's mouse stick or the virtual cursor left it) scrolled the camera under the popup. Edge and right-drag scrolling now only happen while the mouse is the device in use. |
| Live mode doesn't say what RT and R3 do | Added to the live prompts: RT "Sim info" (opens the info panel) or "Next: Job" etc. (its next tab), and R3 "Find Sim" (the cursor back on the active Sim; cursor mode only). Select, Start and the d-pad are still left out to keep the bar short. The prompt bar now wraps onto more rows, evenly filled and stacked upwards, when it's too wide for the screen (`PadMath.WrapRows`, 5 new harness checks, 341 in all). |

| Feedback | Change |
|---|---|
| Holding Select could open the mode menu (the Simitone button's fan of modes) to pick a mode directly, instead of flipping through them | A tap of Select still moves to the next mode (now on release); holding it for 0.6 s opens the mode menu, as clicking the Simitone button does. The d-pad moves between the modes and the household's Sims under them, A picks (a Sim switches control, as clicking a portrait does), B closes it in the current mode. The buttons grow to show focus. The lot helper used to stand down while the menu was open; it now runs a navigator over it (`ModeMenuScope`). The buy/build prompt reads "Mode (hold: menu)". |
| After tabbing out, the controller didn't come back | Not certain yet; the log can't tell which of two causes it was. (1) SDL 2.32 ignores X11 focus events that arrive during a keyboard grab (`NotifyGrab`/`NotifyUngrab`, as a window switcher's Alt+Tab can produce), so it can keep believing the window is in the background; the game then ignores the pad, and SDL itself stops reading it. Fix: the game asks the X server too (`X11Focus`: the root window's `_NET_ACTIVE_WINDOW`, compared with SDL's X11 window from `SDL_GetWindowWMInfo`), and counts as focused if X says it's active (engine: `Game.FocusFallback`, `Game.HasFocus`, used for the input loop and `WindowFocused`). SDL is told to keep reading the pad in the background (`SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS`); the game still ignores it while unfocused. Only the root window is queried, so no X error can end the process. Tested against a real X server (Xvfb) with a real SDL window: 6 harness checks, run when a display is available. (2) Steam keeps the controller on its Desktop Layout after the game comes back to the front; the game can't change that. gamepad.log now says, at each focus change, what X11 thinks is active, when SDL disagrees and the X11 answer is used, when the pad is used while the game thinks it's in the background, and whether pad input arrives after focus comes back (or not within 15 s). |

| Feedback | Change |
|---|---|
| The mode menu works; do the same for the info panel's tabs by holding RT | The shoulder buttons and triggers now go through `PadChords`: each acts when let go, unless another of the four was down at the same time, and RT held for 0.6 s is a hold. RT tap still opens the panel or moves to the next tab; RT held opens the panel's tab fan (the category switcher), in live mode and in buy/build (catalog categories). The d-pad moves between the tabs, A picks one, B closes the fan; the tab buttons recolour to show focus (`CategoryMenuScope`; `UICategorySwitcher.Expanded` added). LB, RB and LT now act on release (a short delay) so the cheat combo below doesn't switch Sims or open panels on the way. |
| A cheat menu like the console games: hold both bumpers and triggers for a few seconds | Holding LB+RB+LT+RT for 1.5 s (L1+R1+L2+R2 on PlayStation, as on PS2) opens Simitone's cheat box with the on-screen keyboard. Closing the keyboard (Done, or B on an empty box) enters what was typed, as Enter does; something that isn't a cheat leaves the box up to fix. |
| The console FISH EYE cheat: Circle to look through the Sim's eyes | Typing FISH EYE (or FISHEYE / FISH EYES / FISH_EYE) toggles the cheat for the session. Then in live mode, B (Circle on PlayStation) switches to FreeSO's Direct camera, which sits in the active Sim's head with the head hidden, and puts the Sim under direct control: the left stick walks relative to the view, the right stick looks around (up to 1.2 rad up or down), and the Sim's body turns to face where you look, as FreeSO's own first person does. The view starts where the Sim faces. Walls go up with roofs and all floors are drawn while in it, as FreeSO does; the walls setting comes back after. B again, or leaving live mode, returns to the normal view. LB/RB still switch Sims (the view moves to the new Sim's head). Needs the 3D renderer. Prompts: right stick "Look", B "Normal view", and B "FISH EYE" while the cheat is on. 16 new harness checks (360 in all), including the camera-yaw maths against the Sim's facing. |

| Feedback | Change |
|---|---|
| Travelling from a lot (the Magic Town hole; vacations and Studio Town work the same way), the town map came up with the lot's UI showing over it, and the pad only had the cursor | The lot shows the destination's map over itself (a VM dialog) and hid the lot, but not its bar and clock, and nothing navigated it. The bar is now hidden while the map is up (and the lot comes back if the trip is called off). The neighbourhood's pad navigation now works on whichever map is up (`TS1GameScreen.ActiveNeighPanel`: the neighbourhood, or the lot's `TravelSelector`): the same lot browsing, prompts, house panel and X cursor mode. Its navigator is made when a travel map appears and removed when it goes. |
| After travelling to another lot, direct control was off again | Each lot gets new lot controls, and they started from the `GamePadDirectControl` setting. The player's choice (X) is now kept for the session, so it carries over to the next lot; until X is pressed, the setting applies. (FISH EYE still ends when the lot changes: B turns it back on.) |
| On the vacation map the name bubble sat beside the focused lot ("Reclaimed Public Beach:") | The bubble keeps clear of other pad targets, and on a town map the lot pictures are big and overlap one another, so the only clear side was to the left. Lot pictures (`IPadLabelCoverable`) may now be covered, so the bubble sits above the lot as elsewhere; real buttons and the prompt bar are still kept clear. The colon some lot names end in is trimmed. |
| Picking a vacation lot left the game on an empty background, stuck | Not the pad: the game's "Go On Vacation - Dress Appropriately" threw (simantics-errors.log: `Outfit.Read` on null in `VMSuitProvider.GetPersonSuitTS1`), so the trip never happened, and the lot was still hidden behind the map's background. Two fixes. Engine (`2fab2fb`): the expansion outfits (formal, swimsuit, pyjamas, winter, high fashion, the party costumes) come from the person's own strings, which a base-game Sim doesn't have; those Sims now keep their daywear, and a suit that can't be found leaves the current outfit on. Simitone: after a lot is picked the lot is shown again straight away (the Sim sets off from it and the new lot loads when they leave), so a trip that fails leaves the player on the lot. |
| The prompt bar sat bottom right on travel maps (vacation, Magic Town...) | It goes bottom right on a lot, and a travel map is shown from a lot; it now counts as a map (bottom centre). |
| Bella hung buying ski wear (simantics-errors.log: `VMTS1PurchasableOutfitHelper.SetSuit`) | Same family as the vacation outfit crash: buying an expansion outfit read the Sim's current one of that kind, which a base-game Sim doesn't have. Engine (`f6d4af4`): a missing outfit reads as none, and the Sim's string table is grown with empty slots up to it so the bought outfit is kept (`STR.SetString` ignores indices past the end; 4 harness checks pin that down). |


Changes in code: `PadMath.FootprintEllipse`, `PadGraphics.GetLine`/`GetBeam`, `UILotControl.OpenPieMenu(fromPad)`, the engine projection above. The harness has 298 checks; the ring ones cover the oval's size for one tile, a 1x2 sofa, small footprints and capped long ones.

### To check on hardware

1. The neighbourhood: the frame and note on houses and round buttons, the hover look (green-tinted caption, no note) on the house panel's Enter Lot / More / option buttons, the same look under the mouse, the house names in the bubble, and the right-hand buttons against the right edge. Then resize the window, or toggle fullscreen, with and without the house panel open.
2. The prompt bar on the neighbourhood, CAS (each mode) and in a lot (live, pie menu, queue, needs panel, buy/build with nothing, an object, then a tool, the catalog).
7. The floor ring on a one-tile object, a sofa or bed, a Sim, a wall mirror and something on a counter, in 2D and 3D, zoomed in and out. A on something with no actions. L3 in live mode.
3. With a PlayStation pad, or `GamePadGlyphs = playstation`: the symbols instead of letters.
4. The keyboard's look, and that no highlight or bubble shows under it.
5. Mouse hover on the neighbourhood buttons, mode buttons and catalog tabs shows their names.
6. The lot toasts (X in 2D, X in 3D).
8. CAS and the family picker: hover tints on Back, Accept, the family buttons, gender, age, skin colour and the tabs, no box or note on them; the name bubble beside, not over, its neighbours (Child, Adult, Male, the bottom-left Back).
9. Neighbourhood: X between browse and cursor; in cursor mode the pointer starts on the focused lot, lots open under it and are named, A opens the house panel, closing it puts the pointer back. The prompt label on each kind of lot and on the buttons.
10. With Steam running and its desktop layout active: Select no longer toggles first-person, the right stick turns the camera without the mouse appearing, triggers don't click. The mouse and keyboard still work normally a moment after letting go of the pad.
