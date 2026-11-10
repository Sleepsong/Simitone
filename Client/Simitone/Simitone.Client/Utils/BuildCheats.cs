using FSO.LotView;
using FSO.LotView.Model;
using System;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// Build cheats in the spirit of The Sims 2's, for full 3D only (2D has sprites for four directions and tile centres):
    ///  - offgrid on/off: objects follow the cursor instead of snapping to the middle of a tile (to a sixteenth of a tile).
    ///  - rotate45 on/off: objects turn 45 degrees at a time instead of 90.
    /// Use with care, like moveobjects: TS1's objects were made for tile centres and four directions, so Sims can take odd
    /// routes to, or fail to use, an object that's off the grid or diagonal. For this session only; objects placed with
    /// them stay where they are (in 2D a diagonal object shows its nearest straight sprite).
    ///
    /// These belong to the optional built in "Build Cheats" pack, which is off by default. Without it the commands are
    /// refused, so neither cheat can be on. Objects already placed with them keep their place either way.
    /// </summary>
    public static class BuildCheats
    {
        public static bool OffGrid;
        public static bool Rotate45;

        /// <summary>Whether a cheat applies now: it's on, and the lot is in full 3D.</summary>
        public static bool OffGridActive(World world) => OffGrid && Is3D(world);
        public static bool Rotate45Active(World world) => Rotate45 && Is3D(world);

        private static bool Is3D(World world) => world?.State != null && world.State.CameraMode == CameraRenderMode._3D;

        /// <summary>
        /// Runs "offgrid on|off" or "rotate45 on|off" (on alone turns it on). Returns false if the command isn't one of
        /// these; done says whether it was understood.
        /// </summary>
        public static bool TryCommand(string command, out bool done)
        {
            done = false;
            var parts = command.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || (parts[0] != "offgrid" && parts[0] != "rotate45")) return false;
            if (!Packs.PackManager.IsActive(Packs.BuiltInPacks.BuildCheats)) return true; //ours, but the pack is switched off
            bool value;
            if (parts.Length == 1 || parts[1] == "on") value = true;
            else if (parts[1] == "off") value = false;
            else return true;
            if (parts[0] == "offgrid") OffGrid = value;
            else Rotate45 = value;
            done = true;
            return true;
        }
    }
}
