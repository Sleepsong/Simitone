using FSO.LotView;
using FSO.LotView.Model;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// Optional ways of placing objects, in the spirit of The Sims 2's build cheats. Full 3D only (2D has sprites for
    /// four directions and tile centres):
    ///  - OffGrid: objects follow the cursor instead of snapping to the middle of a tile (to a sixteenth of a tile).
    ///  - Rotate45: objects turn 45 degrees at a time instead of 90.
    /// Both are off unless something turns them on - Simitone itself has no command for them. The sample pack in
    /// SamplePacks/BuildCheats adds the "offgrid" and "rotate45" console commands that do.
    /// Use with care, like moveobjects: TS1's objects were made for tile centres and four directions, so Sims can take odd
    /// routes to, or fail to use, an object that's off the grid or diagonal. They last for this session only; objects
    /// placed with them stay where they are (in 2D a diagonal object shows its nearest straight sprite).
    /// </summary>
    public static class BuildOptions
    {
        public static bool OffGrid;
        public static bool Rotate45;

        /// <summary>Whether an option applies now: it's on, and the lot is in full 3D.</summary>
        public static bool OffGridActive(World world) => OffGrid && Is3D(world);
        public static bool Rotate45Active(World world) => Rotate45 && Is3D(world);

        private static bool Is3D(World world) => world?.State != null && world.State.CameraMode == CameraRenderMode._3D;
    }
}
