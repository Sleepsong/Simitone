# Add-on Packs

Packs are optional add-ons for Simitone, in the spirit of expansion packs: each one is a folder you
install yourself, and nothing in it is used unless you turn it on.

**Status: stage 1 of 4.** Simitone can find packs, check them, and remember which ones you enabled.
Enabled packs do not change the game yet; the layers below hook in at later stages.

| Stage | What it adds | State |
|---|---|---|
| 1 | Pack folders, `pack.json`, checks, the Packs screen | Done |
| 2 | Content and patch layers (objects, skins, `.piff` patches) | Planned, needs a FreeSO change |
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

## For Simitone developers

The code is in `Client/Simitone/Simitone.Client/Packs/`. `PackManager.Init()` runs once from
`SimitoneGame.Initialize()`, before any content is loaded. Later stages should read
`PackManager.Active`: the enabled packs that passed their checks, fixed for the whole run and ordered
so that each pack comes after the ones it requires.
