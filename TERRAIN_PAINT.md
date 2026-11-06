# Terrain paint

Simitone can paint the ground with the game's own terrain types, as in The Sims 2. Paint a sandy path, a patch of
autumn grass, rock, or snow onto any lot, whatever ground the lot started with.

## Using it

In build mode, open **Terrain**. After Raise/Lower, Flatten and the Grass Tool there's one paint for each terrain type,
then **Remove Terrain Paint**.

| Paint | Ground | Grass blades |
| --- | --- | --- |
| Grass | the usual lawn | tall |
| Dark Grass | darker grass | tall |
| Autumn Grass | brown, autumn grass | tall |
| Sand | sand, as on beach lots | flat |
| Rock | bare rocky ground | flat |
| Snow | snow, as on winter lots | flat |
| Cloud | Magic Town's cloud ground | flat |

- **Painting:** drag over the ground. The brush shows where it would paint before you start, and the paint previews
  as you drag.
- **Brush size:** **+** and **−** change it, from a single point up to a 6-tile radius.
- **Soft brush:** hold **Shift** (the second touch button). It paints only some points around the brush's edge, so
  the paint feathers into what's there.
- **Removing paint:** hold **Ctrl** (the touch erase button), or use **Remove Terrain Paint**. The lot's own ground
  shows again.
- **Cost:** $1 for each point painted, like the Grass Tool.
- **Grass:** the Grass Tool still works on painted ground. Painted grass browns and greens with the grass state, as
  the lot's own grass does.

## Saving

Painted ground is saved with the lot, in a marked block at the very end of the save. Older Simitone builds and the
original game still load the lot normally, but with its unpainted ground. Lots with no paint save nothing extra.

## How it works

In The Sims 1, a terrain type is a set of colours (the light and dark grass colours, and the dry and dead colours
the grass state blends towards) and how tall its grass blades grow. Painting gives each point of the ground its own
type, instead of one type for the whole lot.

**FreeSO** (`Sleepsong/FreeSO`, branch `simitone/terrain-paint`):

- **The paint layer.** `VMArchitectureTerrain.Paint` holds a type for each point, laid out like the grass state;
  255 means the lot's own ground. It has a preview copy, like the heights and grass, so tools can show paint before
  it's placed.
- **The paint command.** `TERRAIN_PAINT` (`VMTerrainPaintTools`) takes a round brush with a radius, a soft edge, and
  a type to paint, or erases.
- **Drawing.** `TerrainComponent` handles painted ground:
  - Painted points get their type's colours, blended between neighbouring points. A painted edge fades over about
    a tile, a bit like The Sims 2's soft brushes.
  - Grass blades are drawn once for the lot's own ground and once for each painted type, each with its own
    colours and blade height. Autumn grass gets autumn blades, and sand gets the same flat speckle as a sand lot.

**Simitone** (`claude/terrain-paint`):

- `UITerrainPainter` and the catalogue entries, with swatch icons drawn from each type's colours.

### Checks

`tools/terrain-paint-check` checks the brushes, removing paint, previews and saving. It needs no game data and no GPU:

```sh
cd tools/terrain-paint-check && dotnet run -c Release
```

## Not yet

- **Textures:** The Sims 2 paints textures, but these are the game's own terrain types, which are colours and grass.
- **Sharp edges:** paint is per point, so edges are about a tile wide. There are no crisp borders inside a tile.
- **Lot thumbnails:** they show painted colours, but only the lot's own grass blades.
- **Touch brush size:** brush size is keyboard-only. On touch, the last size set is used.
