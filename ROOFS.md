# Drawing roofs (concept)

Simitone can draw roofs yourself, as in The Sims 2, instead of only using the game's automatic roof. This is a first
version of the idea. It has three roof shapes, a pitch per roof, and the lot's roof style.

## Using it

In build mode, open **Roofs**. The roof tools come before the roof styles:

| Tool | What it does |
| --- | --- |
| **Hip Roof** | Drag out a roof that slopes on all four sides. |
| **Gable Roof** | Drag out a roof with a ridge along its long side and upright triangular ends. Hold **Shift** (the second touch button) for a ridge along the short side. |
| **Shed Roof** | Drag out a single slope, low on the side you drag towards. Hold **Shift** to make that side the high one. |
| **Remove Roof** | Click a roof to remove it. **Ctrl** (the touch erase button) does the same with any roof tool. |
| **Automatic Roof** | Go back to the game's automatic roof. Drawn roofs are removed, and their price is refunded. |

- **Where roofs sit.** A roof sits on top of the walls of the floor you're looking at. Go up a floor to roof the
  floor above.
- **Eaves.** Roofs overhang the walls by half a tile on their sloped sides. Gable ends and a shed roof's high side
  stand on the walls.
- **Pitch.** **+** and **−** change the pitch of the next roof, from 20% to 150%. The game's usual roof is 66%.
- **Preview.** The roof shows while you hover and drag, with its price ($10 a tile). Removing a roof refunds its price.
- **Style.** Roof styles from the catalogue apply to drawn roofs too.
- **Replacing the automatic roof.** The first roof you draw on a lot replaces the automatic roof. As in The Sims 2,
  the house then has only the roofs you draw. Removing every roof leaves it with none, until you pick Automatic Roof.
- **Overlaps.** Roofs can overlap, and where they meet, they pass through each other.

## Saving

Drawn roofs are saved with the lot, in a marked block at the very end of the save. Older Simitone builds and the
original game still load the lot normally, but with its automatic roof. Lots that keep the automatic roof save
nothing extra.

## How it works

The work is split between FreeSO and Simitone.

**FreeSO** (`Sleepsong/FreeSO`, branch `simitone/roof-tool`):

- `FSO.LotView.Model.DrawnRoofs`: the roofs. Each `RoofPiece` has a floor level, a rectangle of tiles, a shape
  (hip, gable or shed), a direction and a pitch. It also holds the lot's automatic-roof switch, and handles saving.
- `RoofComponent`: meshes each roof from four corners and four ridge points, with each side either sloped or upright.
  - **Hip roofs** reuse the automatic roof's geometry, which now runs through the same code unchanged.
  - **Gable and shed roofs** move the ridge points.
  - **Sloped sides** get the shingles, rims and edge trim.
  - **Upright sides** (gable ends, a shed's high side) are drawn in a plaster colour.
- `VMRoofTools` and three architecture commands:
  - `ROOF_RECT` draws a roof.
  - `ROOF_ERASE` removes the newest roof on a tile.
  - `ROOF_AUTO` switches the automatic roof on or off.

  Tools preview them the way they preview walls and floors.

**Simitone** (`claude/roof-tool`):

- `UIRoofTool`, `UIAutoRoofer` and their catalogue entries, with icons drawn in code.

### Checks

`tools/roof-check` checks the roof shapes, the meshes, the commands and saving. It needs no game data and no GPU:

```sh
cd tools/roof-check && dotnet run -c Release
```

## Not yet

- **More shapes:** dormers, diagonal roofs, and roof shapes beyond hip, gable and shed.
- **Joins:** neat joins where roofs meet. They pass through each other for now.
- **Gable ends:** they're plain plaster. They don't take the wall covering of the walls below.
- **Editing:** resizing or moving a roof after it's drawn. Remove it and draw it again.
- **Touch pitch control:** pitch is keyboard-only. On touch, roofs use the last pitch set.
- **Thumbnails:** lot thumbnails and the neighbourhood view show drawn roofs without their gable ends.
