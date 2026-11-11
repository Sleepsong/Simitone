using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// What a guest checks before playing on a host: the same Simitone version (so both run the same game
    /// logic) and every object the host's game has (so any lot the host plays can be loaded). The guest
    /// having extra objects is fine. User made Sims are not compared; the host sends those.
    /// </summary>
    public static class CoopCompat
    {
        private const int MaxObjects = 1 << 20;

        /// <summary>The GUIDs of every object in this game, other than user made Sims.</summary>
        public static uint[] LocalObjects()
        {
            var objects = FSO.Content.Content.Get().WorldObjects;
            return objects.Entries
                .Where(x => x.Value.Source != FSO.Content.GameObjectSource.User)
                .Select(x => (uint)x.Key)
                .ToArray();
        }

        public static byte[] PackWelcome(uint playerID, string version, uint[] objects)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(playerID);
                    writer.Write(version ?? "");
                    writer.Write(objects.Length);
                    foreach (var guid in objects) writer.Write(guid);
                }
                return stream.ToArray();
            }
        }

        public static void ParseWelcome(byte[] data, out uint playerID, out string version, out uint[] objects)
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                playerID = reader.ReadUInt32();
                version = reader.ReadString();
                var count = reader.ReadInt32();
                if (count < 0 || count > MaxObjects) throw new InvalidDataException("Bad object count " + count);
                objects = new uint[count];
                for (int i = 0; i < count; i++) objects[i] = reader.ReadUInt32();
            }
        }

        /// <summary>
        /// Returns why this game cannot play on that host, or null if it can.
        /// </summary>
        public static string FindProblem(string hostVersion, uint[] hostObjects, string myVersion, IEnumerable<uint> myObjects)
        {
            if (hostVersion != myVersion)
            {
                return "The host is running Simitone " + hostVersion + ", and this is Simitone " + myVersion +
                    ". Both players need the same version.";
            }

            var mine = new HashSet<uint>(myObjects);
            var missing = hostObjects.Where(x => !mine.Contains(x)).ToList();
            if (missing.Count > 0)
            {
                return "This game is missing " + missing.Count + " object" + (missing.Count == 1 ? "" : "s") +
                    " that the host's game has (expansion packs or downloads). Both players need them installed.\n\n" +
                    "First missing: " + string.Join(", ", missing.Take(5).Select(x => "0x" + x.ToString("X8")));
            }
            return null;
        }
    }
}
