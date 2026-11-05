# VR comfort and control research notes

Research notes for Simitone's VR mode (see [VR.md](VR.md)). They cover:
- what makes VR uncomfortable;
- what has evidence behind it;
- how other VR games and flat-to-VR mods handle comfort and controls;
- a proposed design for the hardest problem we have: **letting Sim actions play out without making the player
  sick**.

Nothing here is implemented yet unless it says so. Sources are linked inline and listed at the end. Some came
from search summaries rather than full papers, so treat the exact figures as unverified.

## 1. Summary

- **Motion you don't control is the main trigger.** Stanney & Hash (1998) compared passive, active and coupled
  control: full user control lowered sickness against no control, and coupled control lowered it most.
  ([Stanney & Hash 1998](https://stars.library.ucf.edu/scopus1990/3252))
  - A Sim walking to the fridge while your view rides along in their head is exactly that kind of passive motion.
    It is the riskiest thing our VR mode does today.
- **Ports and mods step outside the character for automated sequences.**
  - The official Resident Evil 4 VR port is first person in play but keeps cutscenes and QTEs third-person, shown
    on a large screen. Its lake boat section lets you pull the camera out to steer in third person, which a
    reviewer said helped with motion sickness.
    ([Road to VR](https://roadtovr.com/resident-evil-4-quest-2-review/),
    [TechRadar](https://www.techradar.com/reviews/resident-evil-4-vr-review),
    [Meta dev blog](https://developers.meta.com/horizon/blog/rebuilding-resident-evil-4-what-it-took-to-bring-capcoms-classic-to-oculus-quest-2/))
  - The Half-Life 2 VR mod offers an optional third-person vehicle camera with comfort options.
    ([vr.org guide](https://vr.org/half-life-2-vr),
    [TechSpot](https://www.techspot.com/news/97985-well-regarded-half-life-2-vr-mod-adds.html))
- **Character games keep your viewpoint still or move it in discrete jumps.**
  - Trover Saves the Universe: you only teleport to warp points the character reaches.
    ([Xbox Tavern](https://www.xboxtavern.com/trover-saves-the-universe-review/),
    [Cog Connected](https://cogconnected.com/review/trover-saves-the-universe/))
  - Moss: "you are the camera", a fixed observer per area.
    ([GamesBeat](https://gamesbeat.com/polyarcs-moss-hides-clever-vr-game-design-under-its-cuteness/))
  - Lucky's Tale: a slow, smooth automatic follow camera.
    ([UploadVR](https://uploadvr.com/luckys-tale-gold-standard-for-third-person-vr-video-games/))
- **Recommendation for Simitone:** when the game takes over your Sim (a queued action, routing, a posture change),
  **detach the view from the Sim's head**. Keep it still, and follow the Sim only with quick fade-out/fade-in
  "blinks", or from a third-person vantage point. Re-enter the Sim's head with a blink when control comes back.
  Keep the current ride-along as an opt-in "intense" setting, with a vignette. Details in
  [section 6](#6-proposed-design-comfort-modes-for-sim-actions).

## 2. Why VR sickness happens (the short version)

- **Sensory conflict and vection:**
  - your eyes see self-motion (optic flow, especially in the periphery) that your inner ear doesn't feel;
  - this is worst when the motion isn't yours to control or anticipate
    ([Stanney & Hash 1998](https://stars.library.ucf.edu/scopus1990/3252));
  - Meta's guidance frames its comfort techniques as ways to reduce vection
    ([Meta locomotion best practices](https://developers.meta.com/horizon/design/locomotion-best-practices/)).
- **Acceleration and rotation hurt more than constant forward motion.** Meta's comfort guide says the key is to
  minimise acceleration, and to make smooth turning opt-in with careful speed and acceleration tuning
  ([Meta locomotion comfort & usability](https://developers.meta.com/horizon/design/locomotion-comfort-usability/)).
- **Head bob, roll and camera shake add motion the body doesn't feel.** Accessibility guidance flags roll-style
  shakes and stacked bobbing as common problems
  ([Game Accessibility Guidelines](https://gameaccessibilityguidelines.com/avoid-or-provide-option-to-disable-any-difference-between-controller-movement-and-camera-movement/)).
- **Low or unstable frame rate and latency make all of the above worse.** Consumer guides consistently list a stable
  frame rate, e.g.
  [VRGearGuide](https://vrgearguide.com/vr-basics-glossary/vr-comfort-settings/).

## 3. Techniques and how strong the evidence is

| Technique | What it does | Evidence / practice | Simitone today |
|---|---|---|---|
| **Player control of all motion** | Only move the view when the player asks | Full control lowered symptoms versus no control in a controlled study ([Stanney & Hash 1998](https://stars.library.ucf.edu/scopus1990/3252)) | Observer: yes. Sim mode: no, the view rides along with queued actions |
| **Dynamic FOV restriction (vignette)** | Narrows the soft-edged view during motion | Sickness "significantly reduced" with little presence loss; many participants didn't notice ([Fernandes & Feiner 2016, IEEE 3DUI](https://displaydaily.com/restricted-fov-eases-vr-sickness/)). Later studies confirm the effect ([Bath thesis](https://researchportal.bath.ac.uk/en/studentTheses/reducing-head-mounted-display-vr-sickness-through-dynamic-field-o/)) | Not yet |
| **Snap turn** | Rotate in fixed steps (30-45 degrees) instead of continuously | Recommended by Meta; standard in comfort menus ([Meta](https://developers.meta.com/horizon/design/locomotion-best-practices/), [VRGearGuide](https://vrgearguide.com/vr-basics-glossary/vr-comfort-settings/)) | Yes (default 30 degrees) |
| **Teleport / blink** | Jump the viewpoint with a fade, with no optic flow | Widely used to cut sickness. It costs some spatial disorientation ([VRCockpit paper](https://arxiv.org/pdf/2205.07041)) | Not yet |
| **Dash** | Very fast continuous move (~10 m/s) instead of a cut | Better path integration than teleport, no difference in sickness (16 people) ([Bhandari et al. 2018](https://graphicsinterface.org/wp-content/uploads/gi2018-22.pdf), [Frontiers 2026](https://www.frontiersin.org/journals/virtual-reality/articles/10.3389/frvir.2026.1760619/full)) | Not yet |
| **Real-world speeds, low acceleration** | Walk about 1.4 m/s, run about 2.8 m/s; avoid speed ramps | Meta's guideline ([Meta](https://developers.meta.com/horizon/design/locomotion-best-practices/)) | Observer walks at 1.6 m/s. The Sim's own speed and acceleration come from FreeSO's direct control (not measured in VR yet) |
| **Rest frame / independent visual background** | Something fixed relative to you (cockpit, grid, clouds, a "nose") | Mixed: an independent background reduced sickness in a driving simulator (Duh et al. 2004), and clouds worked better than a grid; a physical rest frame showed no effect in another study ([summary of the literature](https://pmc.ncbi.nlm.nih.gov/articles/PMC11522211), [Springer](https://link.springer.com/article/10.1186/s41235-024-00593-3)). The "virtual nose" helped in Whittinghill's study but not in a Hamburg replication ([Uni Hamburg](https://www.inf.uni-hamburg.de/en/inst/ab/hci/projects/cybersickness.html)) | Not yet. Our wrist displays and panels are partial rest frames |
| **No head bob / roll** | Don't attach the camera rigidly to an animated head | Developer practice: use head position plus tolerance, not the head bone; never roll ([Unreal forum](https://forums.unrealengine.com/t/real-first-person-camera-without-any-headbobbing/127645), [gamedev.net](https://gamedev.net/forums/topic/669278-ways-to-setup-true-first-person-viewpoint/)) | Partly: the Sim's head position is smoothed and rotation is never taken from the Sim, but the anchor is still the head bone |
| **Third-person for automated movement** | Leave the character's head during sequences the player doesn't drive | RE4 VR (cutscenes, QTEs, boat), HL2 VR (vehicles); see [section 4](#4-how-other-games-and-mods-do-it) | Not yet (this is the proposal) |
| **Easy stairs and slopes** | Teleport nodes for stairs and slopes, gentle elevators | Meta's environment guidance ([Meta](https://developers.meta.com/horizon/design/locomotion-best-practices/)) | Sim mode: the Sim walks the stairs and the view follows (smoothed). Observer: floors change instantly with the stick |
| **Don't push the camera into geometry** | Check moves against walls and characters | Meta locomotion guide ([blog](https://developers.meta.com/horizon/blog/now-available-vr-locomotion-design-guide)) | Observer has wall collision. Sim mode follows the head; the lean allowance can poke into walls |
| **Comfort tiers** | Ship presets: recommended / comfortable / and so on | Meta suggests three tiers ([Meta comfort & usability](https://developers.meta.com/horizon/design/locomotion-comfort-usability/)) | Individual settings only (`vr.ini`) |
| **Seated mode, artificial crouch, swappable movement hand, height** | Accessibility and comfort basics | Road to VR's comfort checklist/glossary ([Road to VR](https://roadtovr.com/vr-comfort-settings-checklist-glossary-developers-players/)) | Height offset: yes. Seated eye height: via `HeightOffset`. Hand swap: no |

## 4. How other games and mods do it

### Flat games converted to VR (mods and ports)

**Vivecraft (Minecraft Java)** ([How to play](https://www.vivecraft.org/how-to-play/), [vivecraft.org](https://vivecraft.org/))
- **Movement:**
  - several locomotion types; the left stick moves and strafes;
  - the trigger switches between teleport and walking forward, and holding Pick Block toggles teleport and free
    move;
  - "Walk Up Blocks" steps you up one block instead of free-move climbing.
- **Comfort for free move:** slower speed, added inertia, and a narrower field of view (vignette).
- **Play modes:** standing and seated (WASD in your look direction).
- **Physical climbing:** ladders and vines are climbed by pulling yourself up.
- **Vehicle Rotation** (2017 release notes): an option, on by default, that turns your view with vehicles you
  don't steer (minecarts, boats, unsaddled horses). Our equivalent is the Sim turning while doing an action: we do
  *not* turn the view, and the research favours keeping it that way, at most as an opt-in.
  ([Vivecraft 1.12.2 v5 notes](https://github.com/jrbudda/Vivecraft_112/releases/tag/1.12.2-jrbudda-5))
- **Community add-ons:** "Steady Sight" smooths the camera over steps and minecart slopes for sensitive players
  ([Modrinth](https://modrinth.com/mod/steady-sight)). ViveMonkeCraft adds Gorilla Tag-style hand-grab movement
  with a vignette and grip smoothing ([modpackindex](https://www.modpackindex.com/mod/156921/vivemonkecraft)).

**UEVR (praydog, Unreal Engine games)** ([GitHub](https://github.com/praydog/UEVR))
- **Third person stays third person:** it doesn't turn third-person games into first person; that is left to
  per-game mods ([360rumors](https://360rumors.com/praydog-uevr/?amp=1)).
- **Camera options:** "Freeze Position" and "Freeze Rotation" were added in 1.03
  ([release notes](https://newreleases.io/project/github/praydog/UEVR/release/1.03)). The decoupled camera pitch
  option stops games forcing the pitch, which users report fixes head-tilt sickness. *Never hand pitch or roll
  control to the game.*
- **Known weak spots:** cutscenes and ladders give odd camera angles in mods
  ([Nexus mod page](https://nexusmods.com/stateofdecay2/mods/1399), [MoguraVR](https://www.moguravr.com/?p=243731)).

**Half-Life 2 VR mod (Source VR Mod Team)**
- vignette, seated support, and a setup wizard that walks players through all the comfort options;
- an optional **third-person vehicle camera** with multiple comfort options, for the airboat and buggy, which "need
  strong VR legs" otherwise;
- teleport movement was added later.
([vr.org](https://vr.org/half-life-2-vr), [TechSpot](https://www.techspot.com/news/97985-well-regarded-half-life-2-vr-mod-adds.html), [Road to VR](https://roadtovr.com/half-life-2-vr-mod-steam-release/))

**Resident Evil 4 VR (official Quest port by Armature)**
- first person in play; cutscenes and QTEs stay third-person and pre-rendered ones are shown on a large screen;
- the Del Lago boat section can be steered in third person to avoid sickness during fast transport;
- mods of the RE remakes use the same idea, switching to an outside view for automated sequences.
([Road to VR](https://roadtovr.com/resident-evil-4-quest-2-review/), [TechRadar](https://www.techradar.com/reviews/resident-evil-4-vr-review), [Game Informer](https://gameinformer.com/2022/01/03/play-resident-evil-2-and-3-remakes-in-vr-with-new-mod-village-vr-mod-in-early-state))

**Skyrim VR**
- an adjustable vignette while running and turning;
- both teleport and direct movement;
- horse riding works for some players and not others.
([Cog Connected](https://cogconnected.com/review/skyrim-vr-review/), [Steam discussions](https://steamcommunity.com/app/611670/discussions/0/2965016081055733266))

### Native VR games where you control a character

- **Lucky's Tale (Playful):**
  - an automatic follow camera, kept slow and smooth;
  - making Lucky smaller relative to the player made the camera move more slowly and smoothly, and was a key
    comfort discovery;
  - they iterated on the camera about a hundred times.
  ([Meta dev blog](https://developers.meta.com/horizon/blog/bringing-the-world-of-luckys-tale-to-life/), [Game Developer](https://gamedeveloper.com/design/how-happy-accidents-made-oculus-platformer-i-lucky-s-tale-i-possible), [UploadVR](https://uploadvr.com/luckys-tale-gold-standard-for-third-person-vr-video-games/))
- **Moss (Polyarc):**
  - you sit in one place and the room is around you;
  - you move on to the next room rather than travelling;
  - "you are the camera"; the hero runs around the diorama in front of you.
  ([GamesBeat](https://gamesbeat.com/polyarcs-moss-hides-clever-vr-game-design-under-its-cuteness/))
- **Trover Saves the Universe:**
  - seated, and you steer a character;
  - you never move smoothly yourself: Trover reaches a warp pad and *you* teleport after him;
  - rated "Comfortable".
  ([Xbox Tavern](https://www.xboxtavern.com/trover-saves-the-universe-review/), [Attack of the Fanboy](https://attackofthefanboy.com/articles/justin-roiland-explains-why-trover-saves-the-universe-wont-give-you-vr-sickness/), [VRDB](https://vrdb.app/game/2718107161580827))

### Builders and god games

- **Little Cities, Townsmen VR, Demeo:** the world is a tabletop model. Hold both grips to rotate, pull, push and
  zoom it by moving your hands. There is no locomotion at all, so there's nothing to get sick from.
  ([MIXED review of Little Cities](https://mixed-news.com/en/little-cities-review/), [UploadVR](https://uploadvr.com/little-cities-review/))

## 5. The Sim-action problem in Simitone

In "play as your Sim" mode the view is anchored to the Sim's head (`VRCameraController.UpdateSimRig`). The game
moves that head without the player's input whenever:

1. **a queued action routes the Sim.** You pick "Eat" on the fridge and the Sim walks across the house, turning
   corners and going through doors. This is the big one: long, passive, with turns and stops.
2. **an animation moves the head in place:** sitting, lying down (sleep, bath, couch nap), bending to pick things
   up, cooking, exercise, dancing, swimming, the hot tub, being pushed aside by another Sim.
3. **a level change happens:** stairs (the Sim climbs them) and teleport-like snaps when routing fails.
4. **someone else drives the Sim:** a visitor's social interaction, the phone ringing, autonomy picking something
   when direct control yields.

What the code can already tell apart:

| Signal | Where |
|---|---|
| The player is steering (direct control frame on top of the Sim's stack) | `VRGameBridge.Steering` |
| The Sim is busy with a queued action (direct control has yielded) | `!Steering` while in Sim mode, or `avatar.Thread.ActiveAction` |
| The Sim is routing (walking somewhere on its own) | a `VMRoutingFrame` on `avatar.Thread.Stack` |
| Posture: standing, sitting, lying | `avatar.GetPersonData(VMPersonDataVariable.Posture)` |
| Level changes and big jumps | `avatar.Position.Level`, and the head target jumping more than 1.5 tiles (already handled as a snap) |

Today we already:
- never rotate the player's view from the Sim (the Sim's body turns, your view doesn't);
- smooth the head position (`SimHeadSmoothing`);
- allow a little lean.

The remaining issue is translation and vertical motion you didn't ask for.

## 6. Proposed design: comfort modes for Sim actions

A new setting, `SimActionView`, with three tiers following Meta's preset idea. Default: **Comfortable**.

### Comfortable (default): "step out and watch"

Like Moss and Trover: your viewpoint never moves continuously unless you are steering.

1. **When control yields** (direct control frame no longer on top, or routing starts):
   - freeze your viewpoint where it is in the world; the rig stops following the head;
   - you are a temporary observer: look around, and optionally move with observer locomotion;
   - show your Sim's head again, so you can watch them walk off;
   - a small marker or outline over the Sim shows who you are.
2. **Follow by blinks:**
   - **When:** the Sim leaves your field of view, goes further than a leash radius (about 4 m), or changes floor.
   - **The blink:** fade to black (~100 ms), move, fade in (~150 ms). Never a continuous move.
   - **Where to:** a vantage point about 1.5-2.5 m from the Sim, on their floor, at your standing eye height,
     facing them.
   - **Checks:** the spot must be in the same room or a doorway (`VMContext.GetRoomAt`) and clear of walls (reuse
     the observer collision).
3. **When control comes back** (steering resumes), or the player pushes the stick or presses B: blink back into
   the Sim's head.
4. **Posture changes** (sit, lie, stand) while you're "in" the Sim: blink to the new height instead of riding the
   sit-down animation. Lying down (sleep, bath, couch): step out and watch rather than lying with them. Sleep
   could fade to a dim "eyes closed" view while time speeds up.

### Moderate: "third-person shoulder view during actions"

Like RE4 VR's cutscenes and HL2 VR's vehicle camera:
- when an action starts, blink to a third-person spot behind and above the Sim;
- from then on, follow with a slow, eased "leash" (Lucky's Tale): the camera only moves when the Sim leaves a
  comfort zone, at capped walking speed, with constant velocity and no rotation;
- the vignette comes on while it moves;
- you blink back into the head when control returns.

### Intense: "ride along" (today's behaviour)

Keep it for experienced players, but harden it:
- **Vignette** whenever the view moves without stick input (dynamic FOV restriction, the best-evidenced single
  technique).
- **Horizontal anchor on the Sim's position, not the head bone.** Use the routing position or pelvis, smoothed,
  with only height from the head (low-passed). This removes the walk-cycle bob and the side-to-side sway.
- **Blink at stairs and level changes** (Meta's "teleport nodes for stairs") and when the head jumps.
- **Cap the camera's speed and acceleration**, and never let it lag and drift after the Sim stops.

### Rules for all tiers

1. **Never rotate the player's view automatically** (yaw, pitch or roll). This is already true; keep it. Any
   "turn with the Sim" option would be opt-in.
2. **Vignette** during any movement the player didn't directly drive. Also consider it while steering, since
   direct control ramps speed up and down (acceleration).
3. **No head bob:** don't anchor to the animated head bone horizontally (applies to steering too).
4. **Keep the camera out of walls** when choosing vantage points and in the lean allowance.
5. **Real-world speeds:** measure the Sim's walk and run speeds in VR metres and compare them with ~1.4 and
   ~2.8 m/s. If they're much faster, scale `WorldScale` or move with blinks.
6. **First-run comfort wizard,** like HL2 VR's: pick a tier, snap or smooth turn, seated or standing, dominant hand.
7. **Rest frame (optional):** a faint floor grid or the wrist and panel elements. Evidence is mixed, and clouds
   beat grids in one study, so off by default.
8. **Frame rate first:** dropped frames make everything worse. Keep `RenderScale` honest on the Steam Frame.

### Implementation sketch (where it goes)

- **`VRGameBridge`:** classify each update as *steering*, *busy-routing*, *busy-in-place* or *posture change*
  from the signals in [section 5](#5-the-sim-action-problem-in-simitone), and tell the camera.
- **`VRCameraController`:**
  - **New states:** `Anchored` (in the head), `Detached` (still) and `Blink` (timed fade, then move).
  - **Detached** reuses the observer rig: freeze `RigPosition`, `RigYaw` and level. `JumpTo`-style placement
    picks vantage points.
  - **Blink:** a full-view black quad drawn with alpha after the world in each eye (`VRLotWorld`, like the
    pointer beam), with the move done at full black.
- **Vignette:** a ring mesh in clip space, transparent at the centre and black at the edge, drawn last in each
  eye. Its inner radius shrinks with how fast the view is moving without input. Settings: `Vignette = Off|Low|High`.
- **Settings:** `SimActionView = Comfortable|Moderate|Intense`, `Vignette`, `LeashRadius`, `BlinkDuration`.

## 7. Control-scheme ideas worth borrowing

| Idea | Seen in | Fit for Simitone | Status |
|---|---|---|---|
| Snap / smooth turn | Most VR games, Meta guidance | Turning in all modes | Done |
| Teleport locomotion (arc) | Vivecraft, Skyrim VR, HL2 VR | Observer mode, as an alternative to smooth movement; reuse the build-mode arc | **Chosen**: offer alongside smooth walking |
| "Go Here" by arc, then follow by blink | Trover (warp pads) | Sim mode: point the arc at the floor, the Sim walks there, you blink after them. A comfortable way to cross a big lot | **Chosen**: offer alongside stick walking |
| Seated mode, artificial crouch | Vivecraft, Road to VR checklist | Seated eye height (we have `HeightOffset`); add a preset | Partly done |
| Swappable movement hand, one-handed play | Road to VR checklist | Left-handed players; controller remapping (SteamVR binding UI helps) | Idea |
| Vignette with strength setting | Vivecraft, Skyrim VR, HL2 VR | All movement, especially Sim actions | Idea (high priority) |
| Third-person view for vehicles and sequences | RE4 VR, HL2 VR | Sim actions ([section 6](#6-proposed-design-comfort-modes-for-sim-actions)), carpools and leaving the lot | Idea (high priority) |
| Hand-grab world move / rotate / zoom (tabletop) | Little Cities, Townsmen VR, Demeo | A "dollhouse" view of the lot, for build mode or just watching: small `WorldScale`, no locomotion, both grips to move, turn and scale the lot | Later, secondary to first person |
| Gravity-gun pick-up, placement arc | Half-Life: Alyx, teleport arcs | Buy/build mode | Done (first version) |
| Wrist displays and menus | Many VR games | Clock and needs | Done; a wrist catalogue tablet is next |
| Physical climbing and pulling | Vivecraft ladders, ViveMonkeCraft | Not a fit for Sims | No |

**The "dollhouse" view deserves a special mention for The Sims.** The game was always played as a god looking down
at a dollhouse. In VR that becomes a tabletop model you lean over, turn and zoom with your hands. It is the most
comfortable possible camera (no locomotion), and it suits watching a household go about its day. Our observer mode
plus a small `WorldScale` gets most of the way there already. What's missing is two-handed grab-to-move, rotate and
scale, and a pointer that works at that scale.

## 8. Decisions and next steps

Decided (November 2026):
- **Movement:**
  - full stick walking stays;
  - **arc movement** (point the arc at the floor: your Sim walks there and you follow by blinks) and **teleport
    movement** (blink to where the arc lands, in observer mode) are added as options alongside it.
- **Comfort tiers in game:** the three `SimActionView` tiers (Comfortable / Moderate / Intense) and the other
  comfort options are changed **in the game, while playing, without restarting**. Today they live in `vr.ini`,
  read at start-up.
  - **Where:** a VR options page that works in the headset: a quick menu on the wrist or floating panel,
    mirrored in the game's own Options panel. The first-run wizard is the same page.
  - **How:** changes apply immediately and are saved back to `vr.ini` (`VRSettings.Save` already exists).
- **Dollhouse view:** later, and secondary to first person.

Next steps, in order (status, November 2026):
1. **Comfortable `SimActionView`:** detach on yield, blink-follow, blink back. *Done.*
2. **Vignette** for any motion the player didn't drive (and optionally while steering). *Done:* `Vignette` and
   `VignetteWhenMoving`, Off/Low/Medium/High.
3. **In-game VR options:** comfort tier, vignette, movement type (stick / arc / teleport), snap or smooth turn,
   seated height. Changes apply live and are saved to `vr.ini`. *Done:*
   - the VR menu (left menu button, left wrist + trigger, or F9);
   - the same options in the game's Options panel (VR button);
   - a first-run comfort setup (tier, turning, movement, standing or seated, vignette), which can be run again.

   Not done: a dominant-hand choice.
4. **Arc movement** (Sim mode) and **teleport movement** (observer), reusing the build-mode arc and the blink.
   *Done:* left trigger.
5. **Posture blinks** (sit, lie, stand) and the "step out" for sleeping and bathing. *Done*, for every tier.
6. **Remove head-bob:** anchor horizontally on the Sim's position. *Done*, for every tier.
7. **Measure** Sim walk and run speeds in VR and tune them (or blink). *Done:*
   - **From the engine:** steered walking is about 1.7 m/s, running about 5.1 m/s, and routed walking about 1.5 m/s,
     at normal game speed; game speeds 2 and 3 multiply these by 3 and 10 (details in
     [VR.md](VR.md#speeds)).
   - **Tuning:** running now needs a stick click; a fast game drops to normal speed while you walk; the Moderate
     glide is 1.6 m/s, so it keeps up.
   - **In play:** `vr.log` records the speeds actually seen.
   - Related, *done:* holding the stick while the Sim is busy cancels their actions so you can take over. It
     follows the "player control" principle: you're never stuck riding along when you want to move.
8. Later: **dollhouse view** (two-hand grab and scale).

### How it was implemented

The design above maps onto the code like this (details in [VR.md](VR.md#how-it-works-for-developers)):

| Design | Implementation |
|---|---|
| Signals: steering, busy, moving, posture | **Steering:** direct control on top of the Sim's stack (`VRGameBridge`).<br>**Walked off:** not steering, and moved 0.6 m from where you last had them. A door or stairs you walked into while steering (its routine runs on top of direct control) doesn't count.<br>**Settled:** position and head height still.<br>**Lying:** head below 50% of the standing head height learnt while steering (kept per Sim), and not lying again until above 62%. |
| Anchored / Detached / Blink states | `VRSimView` in `VRCameraController`: Anchored, Detached (Comfortable, and lying in any tier), Gliding (Moderate). Blinks are a timed fade (40% out, 60% in) with the move made at full black, and the rig held still while fading out. |
| Vantage points | `PlaceNear`: 1.0 m behind the Sim when stepping out, 1.8 m when following, in the direction you're already looking. If that isn't clear, it tries the nearest clear direction (turning you to face the Sim, during the black). It prefers the Sim's room (`VMContext.GetRoomAt`), a floor, and no walls in the way. |
| Follow triggers | Comfortable: beyond `LeashDistance` (4 m), another floor, or another room. Moderate: glide at `GlideSpeed` from 2.6 m down to 1.9 m, and blink when stuck on a wall for 0.6 s or further than 1.5 x the leash. |
| Back into the head | **Steering again:** when you can steer again for 0.2 s.<br>**Comfortable and Moderate:** after at least 1.5 s out, when the Sim is upright and settled with no walk running or queued: sitting for 1 s, or standing for 3 s (so the short stops of cooking or cleaning don't blink you in and out).<br>**Intense:** when they stop lying down. |
| Posture and stairs | **Posture:** a head-height change over 0.3 m holds the old height until the new one has been still for 0.25 s, then blinks there.<br>**Stairs and jumps:** a floor change, or a jump of more than 1.5 tiles, blinks. |
| Vignette | `VRComfortOverlay`, a view-space ring drawn last in each eye. **Driver:** the speed the rig carries your head and the smooth-turn rate. Your own head motion doesn't count, and neither does the lean limit holding your head back in the Sim; snaps and blinks don't count either. After you let go of the stick it keeps the lighter "driving" strength for 0.6 s while the Sim slows down.<br>**Clear area:** from 72 degrees (none) down to 22 degrees (High at full motion). |

## References

- Stanney, K. & Hash, P. (1998). *Locus of User-Initiated Control in Virtual Environments: Influences on Cybersickness.* Presence 7(5). https://stars.library.ucf.edu/scopus1990/3252
- Fernandes, A. S. & Feiner, S. K. (2016). *Combating VR sickness through subtle dynamic field-of-view modification.* IEEE 3DUI. Summaries: https://displaydaily.com/restricted-fov-eases-vr-sickness/ , https://researchportal.bath.ac.uk/en/studentTheses/reducing-head-mounted-display-vr-sickness-through-dynamic-field-o/
- Bhandari, J., MacNeilage, P., Folmer, E. (2018). *Teleportation without Spatial Disorientation Using Optical Flow Cues* (Dash). Graphics Interface. https://graphicsinterface.org/wp-content/uploads/gi2018-22.pdf ; follow-up: https://www.frontiersin.org/journals/virtual-reality/articles/10.3389/frvir.2026.1760619/full
- VRCockpit: *Mitigating Simulator Sickness in VR Games Using Multiple Egocentric 2D View Frames.* https://arxiv.org/pdf/2205.07041
- *Virtual Reality Sickness Mitigation Methods: A Comparative Study in a Racing Game.* https://arxiv.org/pdf/2103.05200
- Independent visual background / rest frames: https://pmc.ncbi.nlm.nih.gov/articles/PMC11522211 , https://link.springer.com/article/10.1186/s41235-024-00593-3 , https://www.faac.com/?p=56843
- Virtual nose replication (Uni Hamburg): https://www.inf.uni-hamburg.de/en/inst/ab/hci/projects/cybersickness.html ; Purdue thesis: https://hammer.purdue.edu/articles/thesis/Mitigating_VR_Cybersickness_Caused_by_Continuous_Joystick_Movement/23668833
- Meta: locomotion best practices https://developers.meta.com/horizon/design/locomotion-best-practices/ ; comfort & usability https://developers.meta.com/horizon/design/locomotion-comfort-usability/ ; locomotion design guide https://developers.meta.com/horizon/blog/now-available-vr-locomotion-design-guide
- Road to VR comfort settings checklist & glossary: https://roadtovr.com/vr-comfort-settings-checklist-glossary-developers-players/
- Game Accessibility Guidelines (camera motion): https://gameaccessibilityguidelines.com/avoid-or-provide-option-to-disable-any-difference-between-controller-movement-and-camera-movement/
- W3C XR Accessibility User Requirements: https://www.w3.org/TR/xaur/
- Vivecraft: https://www.vivecraft.org/how-to-play/ ; release notes (Vehicle Rotation): https://github.com/jrbudda/Vivecraft_112/releases/tag/1.12.2-jrbudda-5 ; Steady Sight: https://modrinth.com/mod/steady-sight
- UEVR: https://github.com/praydog/UEVR ; 1.03 notes: https://newreleases.io/project/github/praydog/UEVR/release/1.03
- Half-Life 2 VR mod: https://vr.org/half-life-2-vr , https://www.techspot.com/news/97985-well-regarded-half-life-2-vr-mod-adds.html
- Resident Evil 4 VR: https://roadtovr.com/resident-evil-4-quest-2-review/ , https://www.techradar.com/reviews/resident-evil-4-vr-review , https://developers.meta.com/horizon/blog/rebuilding-resident-evil-4-what-it-took-to-bring-capcoms-classic-to-oculus-quest-2/
- Lucky's Tale: https://developers.meta.com/horizon/blog/bringing-the-world-of-luckys-tale-to-life/ , https://uploadvr.com/luckys-tale-gold-standard-for-third-person-vr-video-games/
- Moss: https://gamesbeat.com/polyarcs-moss-hides-clever-vr-game-design-under-its-cuteness/
- Trover Saves the Universe: https://www.xboxtavern.com/trover-saves-the-universe-review/ , https://attackofthefanboy.com/articles/justin-roiland-explains-why-trover-saves-the-universe-wont-give-you-vr-sickness/
- Little Cities: https://mixed-news.com/en/little-cities-review/ , https://uploadvr.com/little-cities-review/
- Skyrim VR: https://cogconnected.com/review/skyrim-vr-review/
