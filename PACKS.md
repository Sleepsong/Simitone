# Add-on Packs

Packs are optional add-ons for Simitone, in the spirit of expansion packs: each one is a folder you
install yourself, and nothing in it is used unless you turn it on.

**Status: all four stages built.** Enabled packs can add content (objects, skins, animations, sounds),
patch the game's objects and run their own program code, and optional features built into Simitone
are switched on and off from the same list.

| Stage | What it adds | State |
|---|---|---|
| 1 | Pack folders, `pack.json`, checks, the Packs screen | Done |
| 2 | Content and patch layers (objects, skins, `.piff` patches) | Done |
| 3 | Built-in engine features as switchable packs (weather first) | Done |
| 4 | Code layer (`.dll`), save-game compatibility checks | Done |

## For players

1. Put the pack's folder in `Packs/` inside the Simitone user directory (next to `config.ini`).
   Simitone creates the folder on first start.
2. In a lot, open **Options → Packs** and switch the pack on.
3. Restart Simitone. Changes only ever apply on the next start.

A pack that cannot be used shows the reason in the list (a pack it needs is missing or off, it clashes
with another enabled pack, it needs a newer Simitone, or its `pack.json` is broken).

**Packs with code.** A pack marked **Code** contains a program that can do anything on your computer
that Simitone can. Enabling one asks you to confirm, and only packs you confirmed run their code. If
an update adds code to a pack you already had switched on, the code stays off until you switch the
pack off and on again. Only confirm packs from sources you trust.

**Packs and saved lots.** Simitone remembers which packs each lot was last saved with. If you open a
lot without one of them, it warns you before you play: things the pack added may be missing, and
saving would remove them for good. To keep them, leave without saving, switch the pack back on and
restart.

Your choices are stored in `packs.json` in the user directory.

### Built-in packs

Some features ship inside Simitone but are optional. They appear in the same list, marked
**Built in**, and need no installing.

| Pack | Default | What the switch does |
|---|---|---|
| Weather | On | Switching it off means no rain, snow, hail or storms on any lot, and the `weather` console command is refused. The weather saved with your game is kept, and comes back if you switch the pack on again. |

## For pack authors

A pack is a folder with a `pack.json` at its root:

```json
{
  "id": "example.seasons",
  "name": "Seasons",
  "version": "1.0",
  "author": "You",
  "description": "One line shown in the Packs screen.",
  "minSimitoneVersion": "0.8.22",
  "requires": ["example.core"],
  "conflicts": ["other.weather"],
  "assembly": "Seasons.dll"
}
```

Only `id` is required. It must be unique, at most 64 characters, and use only lowercase letters,
digits, `.`, `_` and `-`. If two folders share an id, the first one (by folder name) keeps it.

| Field | Meaning |
|---|---|
| `requires` | Ids of packs that must be enabled before this one can be |
| `conflicts` | Ids of packs that cannot be enabled alongside this one (applies in both directions) |
| `minSimitoneVersion` | Lowest Simitone version the pack runs on |
| `assembly` | A `.dll` inside the pack folder (the code layer, below) |

Layers are recognised by what the folder contains:

| Layer | Marker | Shown as |
|---|---|---|
| Content | a `content/` folder | Content |
| Patches | a `patch/` folder | Patches |
| Code | `assembly` in `pack.json` | Code |

### Content layer

`content/` is laid out like The Sims' own folder and is read alongside it, using the same rules:

| Put this | Here |
|---|---|
| Objects (`.iff`) | `content/Downloads/` |
| Skins and meshes (`.bmp`, `.skn`, `.cmx`) | `content/GameData/Skins/` |
| Animations (`.cfp`, with their `.cmx`) | `content/GameData/Skins/` or `content/Downloads/` |
| Archives (`.far`) and sound (`.hot` with its `.hit`/`.hsm`) | anywhere under `content/` |

A loose file with the same name as one in the game's folders, or in a pack loaded earlier, replaces
it. Packs load in dependency order, so a pack wins over the packs it requires. As in the game itself,
a file inside a `.far` archive wins over a loose file of the same name; to change an object the game
ships in an archive, patch it instead.

### Patch layer

`patch/` holds `.piff` patches and `.otf` rewrites, in the same format as Simitone's built-in
`Content/TS1Patch/` folder. They are applied after the built-in patches.

### Code layer

Build a .NET 9 class library that references `Simitone.Client`, and name it in `assembly`. Simitone
creates one instance of every public class in it that implements `ISimitonePack` and has a public
constructor without parameters:

```csharp
using FSO.SimAntics;
using Simitone.Client.Packs;

public class SeasonsPack : ISimitonePack
{
    public void OnLoad(Pack pack) { }      // at startup, before game content is loaded
    public void OnContentReady() { }       // content and primitives are loaded: register yours here
    public void OnLotLoaded(VM vm) { }     // each time a lot has loaded
}
```

Implement only the calls you need.

- **Failures are contained.** If the assembly cannot be loaded, or any call throws, that pack's code
  gets no more calls for the rest of the run and the reason is shown in the Packs screen. Its content
  and patches stay in use.
- **Dependencies.** Put any other `.dll` your code needs beside it in the pack folder. Each pack loads
  separately, so two packs can ship different versions of the same library. Do not ship Simitone's
  own assemblies; copies in the pack folder are ignored.
- **Console commands.** Add commands to the cheat console (Ctrl+Shift+C) with
  `PackManager.RegisterCommand` from `OnLoad`. A name that is already taken is refused.
- **Patching the engine.** Simitone ships [Harmony](https://github.com/pardeike/Harmony), so pack code
  can wrap, replace or rewrite any method in Simitone or FreeSO without the engine knowing about it.
  Reference it through `Simitone.Client` and do not put `0Harmony.dll` in your pack. Apply patches in
  `OnLoad`, before the game runs the methods you patch. See the Half Walls sample below.
- **Primitives.** Add SimAntics primitives with `PackManager.RegisterPrimitive` from
  `OnContentReady`. It refuses an opcode that the game or another pack already uses.
- **Threads.** `OnContentReady` runs on the loading thread; the other calls run on the game thread.
- **Saves.** Do not change the lot save format. Keep your pack's data in its own file, as weather does.

### Sample pack: Build Cheats

`SamplePacks/BuildCheats/` is a complete code pack you can install and copy from. It adds two console
cheats in the spirit of The Sims 2's, for full 3D only:

- `offgrid on` / `offgrid off`: objects follow the cursor instead of snapping to the middle of a tile.
- `rotate45 on` / `rotate45 off`: objects turn 45 degrees at a time instead of 90.

To get it, either download the `SamplePack-BuildCheats` artifact from a "Build Simitone" run, or build
it yourself:

```bash
dotnet build SamplePacks/BuildCheats/BuildCheats.csproj -c Release
```

Either way you get a `buildcheats` folder holding `pack.json` and `BuildCheats.dll`. Copy that folder
into Simitone's `Packs/` folder, switch **Build Cheats** on in Options > Packs, confirm, and restart.
Use a pack built from the same version of Simitone you are running.

### Sample pack: Half Walls (experimental)

`SamplePacks/HalfWalls/` adds The Sims 2's half wall to build mode, next to the wall: a low wall you
can wallpaper, which Sims cannot cross but which does not divide a room. Download the
`SamplePack-HalfWalls` artifact, or build it with
`dotnet build SamplePacks/HalfWalls/HalfWalls.csproj -c Release`, and install the `halfwalls` folder.

It is an experiment in changing the engine from a pack. The same feature exists as an engine change
of about 80 lines in 6 FreeSO files; this pack gets the same effect with Harmony patches and no
engine change at all. `HalfWallsPack.cs` goes through it part by part. What it shows:

- Small, self-contained engine edits translate directly (a new wall style, an icon, two properties).
- Edits in the middle of long methods do not. Each needed a workaround from outside: a flag set
  around a call, compiled code rewritten in place, or the compiler's hidden local functions patched.
- The pack depends on the exact shape of the methods it patches. It checks what it can and refuses
  to load, with the reason in the Packs screen, if Simitone's code is not what it was written for.
- One known difference from the engine version: where a full wall runs straight into a half wall, the
  full wall's end above the half wall is left open in 3D.
- Lots saved with half walls need the pack to show them as half walls. Without it they load as walls
  of an unknown style.

### Sample pack: Test Cube

`SamplePacks/TestCube/pack/testcube/` is a complete content pack, ready to copy into `Packs/`: a
`pack.json` and one object, `content/Downloads/TestCube.iff`. The object is a plain grey cube with no
behaviour, in Buy mode under Decorative > Other for 10 Simoleons. It needs no approval, since it has
no code.

The object file is not hand-made. `SamplePacks/TestCube/generator/` writes it with FreeSO's own file
code and draws the sprites by tracing a cube through the engine's camera maths, then reads the file
back and checks it. To regenerate it:

```bash
dotnet run --project SamplePacks/TestCube/generator -- SamplePacks/TestCube/pack/testcube/content/Downloads
```

## For Simitone developers

The code is in `Client/Simitone/Simitone.Client/Packs/`. `PackManager.Init()` runs once from
`SimitoneGame.Initialize()`, before any content is loaded. Later stages should read
`PackManager.Active`, the enabled packs that passed their checks, fixed for the whole run and ordered
so that each pack comes after the ones it requires.

To make a feature built into Simitone optional, add it to `BuiltInPacks.cs` and have its code check
`PackManager.IsActive(BuiltInPacks.YourFeature)`. The answer cannot change during a run. A built-in
pack can be on by default; the ones the user turns off are remembered in `packs.json`, so a feature
added by an update starts in its default state.

The content and patch layers go through two lists in FreeSO's `Content` class, `TS1ExtraRoots` and
`ExtraPatchPaths`, which `PackManager` fills from the active packs before content loads.

Which packs a lot was saved with is recorded by `LotPackRecord`, in `LocalHouse/lotpacks.json` in the
user directory. Built-in packs are not recorded, since a lot loads the same without them.

The FreeSO side of this is on the `simitone/addon-packs` branch of `Sleepsong/FreeSO`, which is where
this branch's `FreeSO` submodule now points.
