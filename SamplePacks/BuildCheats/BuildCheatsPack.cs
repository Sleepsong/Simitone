using Simitone.Client.Packs;
using Simitone.Client.Utils;

namespace BuildCheats
{
    /// <summary>
    /// A sample add-on pack: two console cheats in the spirit of The Sims 2's.
    ///   offgrid on|off    objects follow the cursor instead of snapping to the middle of a tile
    ///   rotate45 on|off   objects turn 45 degrees at a time instead of 90
    /// "on" can be left out. Both work in full 3D only and last until the game closes.
    /// Simitone knows how to place objects this way (BuildOptions) but has no way to switch it on; this pack adds one.
    /// </summary>
    public class BuildCheatsPack : ISimitonePack
    {
        public void OnLoad(Pack pack)
        {
            PackManager.RegisterCommand("offgrid", args => Switch(args, ref BuildOptions.OffGrid));
            PackManager.RegisterCommand("rotate45", args => Switch(args, ref BuildOptions.Rotate45));
        }

        private static bool Switch(string[] args, ref bool option)
        {
            if (args.Length == 0 || args[0].ToLowerInvariant() == "on") option = true;
            else if (args[0].ToLowerInvariant() == "off") option = false;
            else return false;
            return true;
        }
    }
}
