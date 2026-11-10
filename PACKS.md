# Add-on Packs

Packs are optional add-ons for Simitone, in the spirit of expansion packs: each one is a folder you
install yourself, and nothing in it is used unless you turn it on.

**Status: stage 2 of 4.** Enabled packs can add content (objects, skins, animations, sounds) and patch
the game's objects. Engine features and program code in packs come at later stages.

| Stage | What it adds | State |
|---|---|---|
| 1 | Pack folders, `pack.json`, checks, the Packs screen | Done |
| 2 | Content and patch layers (objects, skins, `.piff` patches) | Done |
| 3 | Built-in engine features as switchable packs (weather first) | Planned |
| 4 | Code layer (`.dll`), save-game compatibility checks | Planned |

## For players

1. Put the pack's folder in `Packs/` inside the Simitone user directory (next to `config.ini`).
   Simitone creates the folder on first start.
2. In a lot, open **Options → Packs** and switch the pack on.
3. Restart Simitone. Changes only ever apply on the next start.

A pack that cannot be used shows the reason in the list (a pack it needs is missing or off, it clashes
with another enabled pack, it needs a newer Simitone, or its `pack.json` is broken). Enabling a pack
that contains program code asks for confirmation first.

Your choices are stored in `packs.json` in the user directory.

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
| `assembly` | A `.dll` inside the pack folder (code layer, not loaded yet) |

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

## For Simitone developers

The code is in `Client/Simitone/Simitone.Client/Packs/`. `PackManager.Init()` runs once from
`SimitoneGame.Initialize()`, before any content is loaded. Later stages should read
`PackManager.Active`, the enabled packs that passed their checks, fixed for the whole run and ordered
so that each pack comes after the ones it requires.

The content and patch layers go through two lists in FreeSO's `Content` class, `TS1ExtraRoots` and
`ExtraPatchPaths`, which `PackManager` fills from the active packs before content loads.
