# Expansion travel fixes

Branch `claude/expansion-travel` (engine: Sleepsong/FreeSO `simitone/expansion-travel`). It's based on the fork's own `simitone-forked` (engine 7e76c19), with none of the controller or Discord work. The rounds of testing below were done on an earlier copy built on the controller branch. On 2 November it was rebuilt here, and the travel commits were taken off the controller and Discord branches.

What didn't come across:

- **The pad hook:** the trace of an object the pad can't use ("No actions available") belongs to the controller work. A mouse click on such an object still writes its menu.
- **The error log:** `simantics-errors.log` (`-logs`) comes with the controller work, so this branch's notes go to `travel.log` only.

## The problems

- **Magic Town:** a Sim can't get home. Jumping in the hole and calling from the phone don't work.
- **Studio Town:** a cab called to go home never comes.
- **Downtown:** going home works.
- **Saxophone player (NPC):** a Sim got stuck in the interaction's animation and couldn't be cancelled.

None of these leave anything in `simantics-errors.log`, so no script crashes. The scripts either don't offer the way home or run it to a dead end.

## What's known

FreeSO's emulation of the TS1 "generic call" primitive (`VMGenericTS1Call`) was written around Hot Date's Downtown. Several calls that later expansions use do nothing (they return true and change nothing). Upstream FreeSO's copy is identical, so this isn't something our fork lost:

| Call | Name | Used by (as far as known) |
|---|---|---|
| 3 | Pull Down Taxi Dialog | taxis |
| 13 | Abort Interactions | anything that ends other Sims' interactions with an object or NPC (**now implemented**, see below) |
| 35 | Take Taxi Hook | Studio Town (FreeSO's own comment) |
| 34, 36 | Promote / Demote Fame If Needed | Superstar |
| 37 | Cancel Pie Menu | |
| 38 | Get Tokens From String | Makin' Magic |
| 43 | Family Spells Into Controller | Makin' Magic |
| 22 | Select Downtown Lot | pre-Unleashed travel (TODO in FreeSO) |

Any of these could be where the way home stops, but which one it is (if any) has to be seen in the game: there's no TS1 content in the development environment to read the scripts from.

Also worth knowing: Simitone ignores a request to switch to lot 0 or below (only -1, "home", is translated), so a script asking for a lot Simitone doesn't expect does nothing at all.

## The travel trace

Started with `-travellog` (in Steam: Properties, Launch Options), Simitone writes `travel.log` in the user folder (`~/.local/share/Simitone/` on Linux). It's off by default:

- **Every unimplemented TS1 call**, with the routine and object that made it, the Sims involved, and the temps. The first 3 from each routine are written in full, then repeats are counted.
- **Every call on the travel path** (Change To Lot, building the arriving Sim or family, spawning a date, zoning, vacation lots, Make Selected Sim), with temps before and after and the trip state: transit info, the travelling Sim, the family.
- **Town map dialogs** (Downtown, Vacation, Studio Town, Magic Town, and numeric entry, which TS1 uses for Downtown): when they're shown and what they're answered with.
- **Lot switches:** requested by the scripts, queued, ignored (not a lot number), loaded (home from its saved copy, or another lot from disk), and arrived.
- **Pie menus the player opens**, including empty ones that don't appear (with the pad: "No actions available"). The log lists every interaction the object has (its own and the global ones), and whether it was shown or why not:
  - the action or check routine isn't in the installed content;
  - left out by its flags (visitor, age, pet, debug);
  - the check routine said no, or hid it.

  The same menu on the same object is written once.
- **`dump sims` cheat** (or `simdump`): writes every Sim's animation, what its script is blocked on, its stack and its queue, then the same for the objects and NPCs they're using. Use it while a Sim is stuck.

## First test (Studio Town and Magic Town, 1 November)

The first `travel.log` filled its 20,000-line cap two minutes in: objects ask the zoning of the lot (TS1 call 28) constantly, and every answer was written. It ran out on arrival in Studio Town, before the cab or Magic Town. What it did show:

- **Going to Studio Town works.** The phone's "Go to Studio Lot" plugin (`Phone - Go Studio Plugin`, routine 0x1004) makes the Take Taxi Hook call (35, a no-op here) with temp0 -1 at instruction 66. The Studio car comes (`Car - Studio`, "Get In Cab"), and Change To Lot 85 runs from instruction 12.
- **On arrival,** the Studio Town ped marker (`main - middle ped marker`) builds the Sim (18) and restores their needs (21), then makes Pull Down Taxi Dialog (3), a no-op here.
- **Zoning answers:**
  - the home lot (5, listed as residential in `LotZoning.iff`): 0;
  - Studio Town lot 85 (not listed, so FreeSO guesses): 2;
  - an unlisted lot outside 81–89 (Magic Town is 90–99) is guessed as 1.

  The global checks "Is lot a Residential Lot?" (0x0141) and "Is Lot A Community Lot?" (0x01D5) read these answers. The next log has their code, which shows what each value means.
- **Magic Town's controller** (`Magic Controller`) makes Family Spells Into Controller (43, a no-op here) on init and load.

### Changes for the second test

- **Repeats written once:** a travel call (zoning included) from the same place with the same values is written once. The cap is 80,000 lines, and writes are buffered.
- **Code written once:** the first time a routine makes a traced call, its code is written once, decoded: each instruction's primitive with its operand fields, or the routine it calls, and where it goes on true and false.
- **Scripts followed step by step:**
  - Away from home, an interaction the player picks is followed step by step (up to 5,000 lines): each instruction, what it returned, the temps and locals, calls and returns, and routing results. A loop is written twice per instruction and outcome, then skipped.
  - Interactions it queues are followed too, and so are objects it creates, such as a cab (up to 1,500 lines each).
  - An object that can't be created because it isn't in the installed content is noted.
- **Hidden interactions traced:** away from home, for each interaction a pie menu leaves out because of its check routine, the check is run again with the trace on, and its steps are written under it.
- **Clicks that open no menu:**
  - With the pad, an object with no actions ("No actions available") never asked for a pie menu, so the first trace had nothing on Magic Town's hole and phone. It now writes the empty menu.
  - A click on a disabled object is written too.

## Second test (1 November)

### Magic Town: the travelling Sim was a visitor (fixed)

On lot 96, the trace explained the empty menus:

- **The hole** (`A Hole in the Ground`, the Magic Town portal): "Jump In", "Go Home" and "Go Elsewhere" were left out by their flags, not by a check routine. FreeSO counts a Sim as a visitor when their person type is 1 and their greet status is under 2. Bella's person type was 1, and the Magic Town ped marker (`main - middle ped marker`) sets the arriving Sim's greet status to 1. None of those three interactions allow visitors, which shows the original game doesn't count the travelling Sim as one.
- **Why person type 1:** a Sim created on a lot takes person type 0 if their family is the lot's family, else 1 (`VMAvatar.InheritNeighbor`). Both ways a Sim arrives made the Sim first and set the lot's family after:
  - Build The Downtown Sim (18): Downtown, Studio Town;
  - Build Vacation Family (26), one Sim: the Magic Town hole.

  So the travelling Sim was always created as a visitor. Arriving with the whole family set the family first, so that path was fine. **The family is now set before the Sim is created** in both paths.
- **The phone** (`The Magic of Telephony`): "Go Home" and "Go Home With Autofollow Sim" are left out by their check routines ("Go Home - TEST", "Go Somewhere Else - TEST"). Both need simulation global 34 to be 1. It was 0 after arriving through the hole. FreeSO's notes say Magic Town sets 34 to 0 for one Sim and 1 for the whole family, so the phone's way home is probably for a family that came by balloon, and a Sim who came through the hole goes back through it. Not changed.

The zoning question is answered as well. The code of "Is lot a Residential Lot?" (global 0x0141) tests the zoning call's answer for 0, and "Is Lot A Community Lot?" (0x01D5) tests it for 1. So FreeSO's values (0 residential, 1 community) are right. The other lot types are tested by house number, not zoning:

- Downtown: 21–31
- Vacation: 40–49
- Studio Town: 81–89
- Magic Town: 90 and up

### Studio Town: the cab can't be placed (not fixed yet)

The phone's "Go Home" (`Phone - Studio Town`):

- runs "Auto Call Cab", talks to the Studio plugin, and shows the cab dialog;
- creates the Studio car (`Car - Studio`, 24 tiles) out of the world.

The car's main finds it isn't on a residential lot and was requested, and runs "Home Main". That finds a 0x865A6812 object on the lot and asks Find Location For (mode 3: along that object's facing) to place the car there. FreeSO refuses every spot, so the car deletes itself (`remove_object_instance`), and the cab never comes. This happened on both tries.

Why isn't known yet. On lot 85, a `Car - Studio` and a `Car - Studio Carpool` were already running scripts after Bella arrived. If the car that dropped her off is still parked on that road, it would block every spot. The trace now records each spot Find Location For tries in a followed script, why it was refused (with the blocking object, if any), and where the reference object is and which way it faces.

## Third test (1 November)

Downtown and Old Town still work.

### Magic Town: still a visitor (second fix)

The hole's interactions were still left out by their flags. Making the family the lot's family first wasn't enough. When a Sim is created, its init tree runs before `InheritNeighbor` copies the neighbourhood data, and `InheritNeighbor` only works out family or visitor when the person type is still 0. Anything the init tree set was kept, so a Sim whose init set 1 stayed a visitor.

A member of the lot's family is now family (0) whatever was set before. And the Sim built for a trip (18, and 26 for one Sim) is made family if they arrive as anything else, which the trace notes. The pie menu entry now gives the Sim's person type, greet status, family and age, so the next log shows whether that was it.

### Studio Town: the drop-off car is in the way

The trace now has Find Location For's attempts for the first cab:

- **The marker:** `*Portal - Car` (object 927) at tile (38,25), facing north, on a 50×50 lot.
- **The search:** mode 3 tried the line x = 38 from y = 10 to 40. Every spot was blocked:
  - inside the building: the spa tub, glass door, oxygen bar, counters, banners and lamps;
  - at y = 35–40, where the drive is: a parked `Car - Town Car` (rows 0–5 at x = 38).
- **What the player saw:** a car on the street at the first call.

So the car that dropped the Sim off was still parked where the cab has to go. With nowhere to put it, the cab deleted itself.

The second call's cab never got as far as placing itself. "Home Main" waits about 560 ticks (a random number from the Studio plugin's "CT - Plugin Call", × 20) before it looks for a spot, and the game left the lot about 13 seconds after the call. So the second try is inconclusive. It may well have worked: the street was empty by then.

What's left is why the drop-off car stays. The trace now dumps whatever refuses a placement: its stack, queue and position. If the next cab fails, the log says what that car is doing.

## Fourth test (1 November)

Neither worked yet.

### Magic Town: family Sims are never visitors (third fix)

The pie menu entry now shows Bella's data on lot 96: person type 1, greet status 1, family 5. At home it's 0, 0, 5. The trip fixes didn't log a correction, so she arrived as family (0), and something on the lot made her a visitor afterwards. The traced code doesn't show where:

- the hole's "Jump out of hole" sets greet status (34) to 1;
- the Magic ped marker does too, and sets global 34 to 0 for one Sim or 1 for a family.

Rather than chase the script, the visitor rule now leaves the family being played out entirely: a Sim whose family number is the lot's current family is never a visitor, whatever their person type. Visitors from other families are unchanged, and so is anyone at home. Any change to a family Sim's person type is now noted in `travel.log`, with the script that made it.

### Studio Town: the cab sits off the drive (fallback)

All three cabs failed the same way. The trace of what was in the way shows the drive:

- **Passing cars** (`Car - Town Car`, `Car - Standard Carpool`, in their "drive away") were at x = 38.5, y = 37.5, facing north. So cars use a lane down x = 37–39 to the car portal at (38,25).
- **The obstacles** were beside the lane: an oxygen bar at x = 36, and spa tubs at (40,27) and (40,29).
- **The cab:** a multitile object is placed by its lead tile (`Row 0 Tile 0`, offset 0,0), and its other tiles are offset from that. So the 3-wide Studio car covered x = 38–40 and hit the spa tubs on every spot.

Find Location For mode 3 now has a fallback. When every spot along the vector is refused, it searches again with the object centred across the vector (x = 37–39 here), then shifted a tile either way. Placements that already worked are unchanged, because the fallback only runs after the usual search has failed. The attempts in the trace now show positions to fractions of a tile.

## Fifth test (1 November): working

- **Magic Town:** a Sim who came through the hole can go home through it.
- **Studio Town:** the cab comes and takes the Sim away, from lot 85 and lot 88.
- **Downtown:** the cab home still works.

What the log showed:

- **Bella's own script made her a visitor.** Her "person main loop" (0x205B) sets her person type to 1 a second after she arrives on any lot that isn't home (also `person main` 0x2001, Downtown). So the original game counts the travelling Sim as person type 1, and its visitor rule can't be FreeSO's "person type 1 and greet status under 2". The family rule (the family being played is never a visitor) is the right fix, not a workaround. The person-type fixes at creation are harmless but not what fixed it.
- **Lot 85:**
  - The car portal is at (38.5, 25.5), inside the spa building's footprint. The first cab failed while a passing Carpool sat at (38.5, 37.5), even after the centred and shifted searches.
  - The second cab was placed at (38.5, 37.5) by the usual search, the same spot the lot's passing cars use in FreeSO. That's the paved area beside the building, not the street, so it looks wrong.
- **Lot 88:** the portal is at (29.5, 7.5) facing south, and the cab was placed a tile from it, on the road.

### Open: where cars park on lot 85 (map in the next trace)

FreeSO's search for mode 3 goes both ways along the portal's facing, at most 15 tiles, starting with the side its table calls forward. That table has north as +y, opposite to FreeSO's other direction tables (`VMDrop`: north is -y). On lot 85 it finds the paved area 12 tiles behind the portal. A search that went the way the portal faces, as far as the lot edge, might find the street instead, but that's a guess, and it could move cars on other lots.

From the screenshot, the street runs alongside the parked cab, further out than the drive. FreeSO's search stays on the portal's line, so it never reaches it. Placement is unchanged for now. A placement in a followed script that fails, or lands more than 3 tiles from its reference object, now adds a map of the tiles around the reference to `travel.log`:

- **The objects map:** the reference, where the object went, Sims, cars, other objects, and empty tiles marked buildable or not.
- **The floor map:** the floor pattern on each tile, which should show the road.

One cab call on lot 85 is enough to read where its street is.

## Abort Interactions (13)

It was a no-op. It now cancels every Sim's interactions with the stack object, the way FreeSO already does when a Sim leaves a TSO lot (`LeaveLot`):

- Queued interactions are dropped.
- A running interaction gets the cancel flag, which its script checks to wind down.

The stack object's own queue is left alone, and so is the interaction making the call. This may be what stranded the Sim with the saxophone player: if the player's script gives up and asks to abort everyone's interactions with them, a Sim mid-interaction was never told. Unconfirmed until it happens again with the trace on. Each call is written to `travel.log` with how many interactions it cancelled.

## Testing

After the second and third tests' fixes, check that Downtown still works too: its Sim is now family, not a visitor, there as well.

In Studio Town, give the cab time. Its script waits about 10–20 seconds at normal speed before it arrives, and it can't come while the drop-off car is still on the street.

1. Start Simitone with `-travellog` (in Steam: Properties, Launch Options). Delete the old `travel.log` first, so the new one is easy to read.
2. Play from a family's home lot and travel to Studio Town. On arrival, go straight to the way home: call the cab, then wait a couple of in-game hours.
3. Go to Magic Town. Click the hole and the phone, even if no menu appears. Try any other way home.
4. Avoid other interactions on those lots: everything picked away from home is followed, and that fills the log.
5. If a Sim gets stuck, open the cheat box and enter `dump sims`.
6. Attach `travel.log`.
