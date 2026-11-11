# Custom Neighborhoods

Simitone can keep several neighborhoods side by side, create new empty ones, and install
neighborhood packages made by other players.

## Playing

On the neighborhood screen, the **...** button on the left opens the neighborhood list.

- **Play** switches to the selected neighborhood. The one you used last is loaded on startup.
- **New** creates an empty neighborhood from the template that ships with The Sims.
- Entries starting with **Install:** are packages found in your `Neighborhoods` folder.
  Installing one creates a new neighborhood from it and switches to it. The package itself
  is left untouched, so it can be installed again for a fresh copy.

The button is hidden while a new family is waiting to be moved in.

## Where things live

Everything is in the Simitone user directory (`Documents/Simitone` on Windows).

| Path | Contents |
| --- | --- |
| `UserData`, `UserData2`, `UserData3`, ... | One neighborhood (save slot) each |
| `Neighborhoods/<package>/` | Neighborhood packages waiting to be installed |
| `neighborhoods.json` | Remembers which neighborhood was played last |

The stock neighborhoods from The Sims install appear in the list too, and are copied into
the user directory the first time they are played. To delete a neighborhood, delete its
`UserData` folder while the game is closed.

## Making a package

A package is a folder inside `Neighborhoods` containing a `neighborhood.json`:

```
Neighborhoods/
  PleasantValley/
    neighborhood.json
    map.png
    waves1.png
    waves2.png
    UserData/            (optional)
      Neighborhood.iff
      LotLocations.iff
      LotZoning.iff
      StreetNames.iff
      Houses/
      Characters/
```

- **`UserData/`** is the neighborhood players start with: its lots, families, lot positions
  and zoning. The easiest way to make one is to build the neighborhood in Simitone and copy
  its `UserData` folder into the package. Without it, the package starts from the empty
  template and only changes the name and map screens.
- Every other file in the package is copied into the new neighborhood, so images can sit
  next to the manifest or in subfolders.

### neighborhood.json

```json
{
  "name": "Pleasant Valley",
  "author": "Your Name",
  "description": "A quiet valley with a river.",
  "areas": {
    "4": {
      "graphic": "map.png",
      "scale": 2,
      "music": "bkground_nhood1",
      "ambience": "river_loop",
      "pulsate": false,
      "frameDuration": 15,
      "animations": [
        { "x": 0, "y": 0, "frames": [ "waves1.png", "waves2.png" ] }
      ]
    }
  }
}
```

Only `name` is really needed. `areas` replaces the map screen of individual areas:

| Key | Area |
| --- | --- |
| `4` | Main neighborhood |
| `2` | Downtown |
| `3` | Vacation Island |
| `5` | Studio Town |
| `7` | Magic Town |

Each area accepts the following, all optional. Anything left out keeps the stock value.

| Field | Meaning |
| --- | --- |
| `graphic` | Background image, 800x600. A file in the package (PNG recommended), or a path inside The Sims' own content. |
| `scale` | How much house thumbnails are shrunk. Stock screens use `1`; the main neighborhood uses `2`. |
| `music` | Sound event for the music. |
| `ambience` | Sound event for a looping background sound. |
| `pulsate` | `true` plays animations forwards then backwards, `false` loops them. |
| `frameDuration` | How long each animation frame is shown, in 60ths of a second. |
| `animations` | Image layers drawn over the map. Each has `frames` and an optional `x`/`y` offset. Setting a `graphic` without `animations` removes the stock ones. |

Where houses sit on the map is not part of the manifest: it comes from `LotLocations.iff`
in the neighborhood's `UserData`, as in The Sims.

## Current limits

- House numbers still decide which area a lot belongs to (21-31 Downtown, 40-49 Vacation,
  81-90 Studio Town, 91-99 Magic Town), so packages cannot add new areas yet.
- There is no in-game editor for lot positions or for deleting and renaming neighborhoods.
