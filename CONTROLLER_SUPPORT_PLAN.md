# Controller Support Plan (PS2-style)

**Status:** Phase 0, design only. No code changes yet.
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

`FreeSO` is a git submodule that points at `alexjyong/FreeSO`. Engine changes must either land in a FreeSO fork we control (with `.gitmodules` repointed) or be upstreamed to that repo. That is one reason the plan keeps engine changes small and generic.

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

## 6. Proposed default mapping

This is a *proposal* in the spirit of the console games. It is not a verified reproduction of any one title's layout.

**Live mode**

| Input | Cursor mode | Direct control |
|---|---|---|
| Left stick | Move the selector | Walk (run at full tilt) |
| Right stick | Pan the camera | Pan the camera |
| A / Cross | Interact, or Go Here on empty floor | Interact with the target |
| B / Circle | Cancel / close | Cancel / close |
| X / Square | Toggle Cursor ↔ Direct | Toggle Cursor ↔ Direct |
| Y / Triangle | Focus the action queue | Focus the action queue |
| LB / RB (L1 / R1) | Previous / next household Sim | same |
| LT / RT (L2 / R2) | Zoom out / in | same |
| D-pad ← / → | Game speed down / up | same |
| D-pad ↑ / ↓ | Floor up / down | same |
| L3 | Cycle walls (up / cutaway / down) | same |
| R3 | Recentre and re-anchor on the Sim | same |
| Start | Pause + options | same |
| Back / Select | Mode switcher (Live / Buy / Build) | same |

**Pie menu:** the stick or D-pad picks a slot, A chooses it, B goes back or closes, and LB/RB page through overflow items.

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
| 0 | This plan, decisions in §8, FreeSO fork | Decisions recorded |
| 1 | E1, E2, E3 | <ul><li>Every screen can be reached with a pad via the virtual cursor.</li><li>Mouse/keyboard behaviour is unchanged.</li><li>Switching device mid-game works.</li></ul> |
| 2 | E5; S1 (camera + cursor mode); S2; S3 (Sim cycling / speed / mode) | A household can play a full day in live mode without touching the mouse |
| 3 | E4; S3 (panels); S5 | <ul><li>Every live-mode panel and VM dialog can be navigated.</li><li>The virtual cursor is no longer needed inside a lot.</li></ul> |
| 4 | E6; S1 (direct control) | <ul><li>The Sim can be walked through doors and stairs and can interact.</li><li>Autonomy doesn't fight the stick.</li><li>Save and load round-trip cleanly.</li></ul> |
| 5 | S4 | Buy, place, rotate, sell, walls, floors and wallpaper all work with a pad only |
| 6 | S6, S7, S8, on-screen keyboard | Neighbourhood → CAS → lot works without a mouse |

Phase 1 is the right first PR. It is small, stays inside the engine, is testable, and everything else depends on it.

---

## 8. Decisions needed

1. **Where FreeSO changes land:** a FreeSO fork we own, or PRs to `alexjyong/FreeSO`.
2. **Default live scheme.** Recommendation: **Cursor mode**. It works with every object and doesn't depend on the VM changes in Phase 4.
3. **Layout under controller** (S8). Recommendation: the mobile-style layout, once layout is separated from input source.
4. **2D vs 3D.** Everything above works in both. Direct control plays better in 3D, where a follow camera can sit behind the Sim. Decide whether direct control is 3D-only in v1.

---

## 9. Risks and things to verify early

- **Autonomy vs direct control.** The frame yields whenever any non-idle action is queued (`VMDirectControlFrame.cs:637`). If free will queues something for the controlled Sim, the player loses control mid-walk. Autonomous queueing needs to be suppressed for the active Sim while the stick is held, plus a short grace period. Prototype this first in Phase 4.
- **BHAV 9001 via PIFF.** Confirm that PIFF `Add` works for semi-global files, and that `GetRoutineWithOwner` resolves `0x2329` against TS1's person semi-global.
- **`PersonData[54]` in TS1.** If we keep using it at all, verify that no TS1 behaviour reads or writes that slot.
- **Windows backend.** Windows defaults to WindowsDX (`Simitone.Windows/Program.cs:52`). Its `GamePad` support is expected to be XInput-only, so PlayStation pads may need Steam Input or DS4Windows there; DesktopGL (SDL2) should recognise them natively. Verify on hardware. If confirmed, document the existing `gl` launch argument for PlayStation pads.
- **Pie menu anchor.** The menu is currently placed at the mouse. With E5 it will be placed at the target's projected screen position, which has to be clamped to the screen bounds.
- **Text input.** No on-screen keyboard exists. Naming, cheats and catalog search all depend on a keyboard.
