# Controller Support Plan (PS2-style)

**Status:** Phases 1 and 2 are done. The engine changes are on `simitone/controller-phase2` in `Sleepsong/FreeSO` (which includes Phase 1), and the submodule points at it (see §10 and §11). Neither phase has been tested on hardware or in the running game yet.
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
| Left stick | Move the selector | Walk (run at full tilt) |
| Right stick | ↑↓ zoom, ←→ rotate | same |
| A / Cross | Interact, or Go Here on empty floor | Interact with the target |
| B / Circle | Cancel / close | Cancel / close |
| X / Square | Toggle Cursor ↔ Direct (Phase 4) | Toggle Cursor ↔ Direct |
| Y / Triangle | Focus the action queue | Focus the action queue |
| LB / RB (L1 / R1) | Previous / next household Sim | same |
| LT / RT (L2 / R2) | Open/close the needs panel / next panel tab | same |
| D-pad ← / → | Game speed down / up | same |
| D-pad ↑ / ↓ | Floor up / down | same |
| L3 | Cycle walls (up / cutaway / down) | same |
| R3 | Recentre and re-anchor on the Sim | same |
| Start | Pause / resume | same |
| Back / Select | Next mode (Live → Buy → Build → Options) | same |

**Pie menu:** the stick points at a slot; the D-pad and LB/RB step through every item, including overflow items and Back; A chooses; B goes back a level or closes.

**Buy/Build:**

| Input | Action |
|---|---|
| Left stick | Scroll under the centre cursor |
| A | Place (hold to drag) |
| B | Cancel, or back to the catalog |
| LB / RB | Rotate |
| Y | Sell / delete |
| X | Eyedropper |
| D-pad | Navigate the catalog when it's open |

---

## 7. Phases

| Phase | Deliverable | Done when |
|---|---|---|
| 0 ✅ | This plan, decisions in §8, FreeSO fork | Decisions recorded |
| 1 ✅ | E1, E2, E3 | <ul><li>Every screen can be reached with a pad via the virtual cursor.</li><li>Mouse/keyboard behaviour is unchanged.</li><li>Switching device mid-game works.</li></ul> |
| 2 ✅ | S8 (mobile layout without touch input); E5; S1 (camera + cursor mode); S2; S3 (Sim cycling / speed / mode) | A household can play a full day in live mode without touching the mouse |
| 3 | E4; S3 (panels); S5 | <ul><li>Every live-mode panel and VM dialog can be navigated.</li><li>The virtual cursor is no longer needed inside a lot.</li></ul> |
| 4 | E6; S1 (direct control, 3D camera only) | <ul><li>The Sim can be walked through doors and stairs and can interact.</li><li>Autonomy doesn't fight the stick.</li><li>Save and load round-trip cleanly.</li></ul> |
| 5 | S4 | Buy, place, rotate, sell, walls, floors and wallpaper all work with a pad only |
| 6 | S6, S7, on-screen keyboard | Neighbourhood → CAS → lot works without a mouse |

Phase 1 is the right first PR. It is small, stays inside the engine, is testable, and everything else depends on it.

---

## 8. Decisions (recorded)

1. **Where FreeSO changes land:** your fork, `Sleepsong/FreeSO`.
2. **Default live scheme:** Cursor mode.
3. **Layout under controller:** the mobile layout. Because of this, separating layout from touch input (S8) moves into Phase 2, since the Phase 2 lot work is built on that layout.
4. **Direct control:** 3D camera only in v1.

---

## 9. Risks and things to verify early

- **Autonomy vs direct control.** The frame yields whenever any non-idle action is queued (`VMDirectControlFrame.cs:637`). If free will queues something for the controlled Sim, the player loses control mid-walk. Autonomous queueing needs to be suppressed for the active Sim while the stick is held, plus a short grace period. Prototype this first in Phase 4.
- **BHAV 9001 via PIFF.** Confirm that PIFF `Add` works for semi-global files, and that `GetRoutineWithOwner` resolves `0x2329` against TS1's person semi-global.
- **`PersonData[54]` in TS1.** If we keep using it at all, verify that no TS1 behaviour reads or writes that slot.
- **Windows backend.** Windows defaults to WindowsDX (`Simitone.Windows/Program.cs:52`). Its `GamePad` support is expected to be XInput-only, so PlayStation pads may need Steam Input or DS4Windows there; DesktopGL (SDL2) should recognise them natively. Verify on hardware. If confirmed, document the existing `gl` launch argument for PlayStation pads.
- **Neighbourhood person data is truncated (pre-existing, unverified impact).** `NBRS` loads only the first 88 person-data entries, although non-version-4 records store 256. On save it writes zeros for everything past 88. If the original game keeps anything there, saving in Simitone wipes it. This is worth checking separately; it isn't related to controller support.
- **New (2026) Steam Controller.** SDL 2.32.2 predates it and has no driver or mapping for it. Through Steam Input it appears as a "Steam Virtual Gamepad", which SDL's mapping database covers, so launching Simitone from Steam is the supported route.
- **Steam Controller (original 2015 model) without Steam.** With Steam closed it presents as a keyboard and mouse ("lizard mode"), not a gamepad, so no code here sees it.
  - SDL 2.32.2, which MonoGame bundles, has a Steam Controller driver, but it is off by default (`SDL_JOYSTICK_HIDAPI_STEAM`). In that version its packet reader only accepts Bluetooth LE reports, so it wouldn't help over the USB dongle or a cable.
  - Practical routes: add Simitone to Steam as a non-Steam game (Steam Input presents a standard gamepad), or run a standalone driver that creates a virtual gamepad.
- **Pie menu anchor.** The menu is currently placed at the mouse. With E5 it will be placed at the target's projected screen position, which has to be clamped to the screen bounds.
- **Text input.** No on-screen keyboard exists. Naming, cheats and catalog search all depend on a keyboard.

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

**Selector marker:** a ring on the floor under the target, with a bobbing diamond above it.
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
| `PadMath`, `PadGraphics` (new) | Selector geometry (screen ↔ tile movement, safe-area overflow, footprint distance, menu clamping) and the procedurally drawn ring and diamond |
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

### Fixes after the first hardware test

- **Exception popups when the selector sat on a Sim.** The selector re-checked its target's interactions twice a second. For a Sim, that runs every social interaction's check tree, and Makin' Magic's "Magic - Attraction" expression (reached from "Kiss Cheek TEST") read an avatar slot that doesn't exist. `VMAvatar.GetSlot` had no bounds check, so each re-check threw and raised another "SimAntics Exception" dialog. Two fixes:
  - Engine `f6741e2`: out-of-range avatar slots now read as empty, as they already did for objects.
  - The selector checks a target's interactions once, when it lands on it (as mouse hover does).
  - Side effect: the marker colour can be stale until the selector moves to another target or the active Sim changes.
- **More exception popups (second test).**
  - **"MAGIC - Ghost Social Test" (from "Tease Scare TEST").** When a Sim inherited its neighbourhood record, its 101-entry person-data array was replaced by the record's 88 entries, and this check reads past entry 87. Engine `e1e26f5` overlays the record onto the full-size array instead. This happens for the mouse too, whenever a Sim's menu is built (hover or click), so it isn't controller-specific.
  - **Sofa and bench threads failing in `VMThread.Pop`** (null reference, empty stack). Not reproduced or explained yet. They appeared alongside Sim errors that reset the Sim, so they may be a knock-on effect of resetting a Sim mid-sit. Recheck after the person-data fix.

### Notes for Phase 3

- Native dialog navigation (E4 focus navigator) is now the main gap. Every VM dialog currently falls back to the virtual cursor.
- The needs panel opens and changes tabs natively, but its contents (relationship entries, job buttons, the inventory) still need focus navigation.
