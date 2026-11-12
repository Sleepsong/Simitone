using FSO.Common;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// The part of the host's game that lives outside the lot: the neighbourhood (families, relationships,
    /// inventories) and the user made Sims. The lot's scripts read and write these all the time, so a guest
    /// needs the host's copy or the two games drift apart.
    ///
    /// The host captures it at the same moment as each lot state it sends. The guest unpacks it into a scratch
    /// neighbourhood folder next to its own (never over it) and loads that; its own neighbourhood is loaded
    /// again when it leaves.
    /// </summary>
    public class CoopWorld
    {
        /// <summary>Files by path relative to the neighbourhood folder, with forward slashes.</summary>
        public Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
        public ushort GameStateFamily = NoFamily; //TS1GameState.ActiveFamily
        public uint DowntownSimGUID;
        public short LotTransitInfo;
        /// <summary>The active members (FAMI.RuntimeSubset) of each family that has any. Not part of the neighbourhood file.</summary>
        public Dictionary<ushort, uint[]> ActiveMembers = new Dictionary<ushort, uint[]>();

        public const ushort NoFamily = 65535;
        private const int MaxFiles = 4096;
        private const int MaxUnpackedSize = 256 * 1024 * 1024;

        /// <summary>The neighbourhood slot guests load the host's neighbourhood into: the folder "UserData9999".</summary>
        private const int GuestSlot = 9998;

        private static readonly string[] StaticFiles =
        {
            "LotLocations.iff", "LotZoning.iff", "StreetNames.iff",
            "Houses/NeighborhoodDesc.iff", "Houses/STDesc.iff", "Houses/MTDesc.iff"
        };

        #region Wire format

        public byte[] Pack()
        {
            using (var output = new MemoryStream())
            {
                using (var zip = new GZipStream(output, CompressionLevel.Fastest))
                using (var writer = new BinaryWriter(zip))
                {
                    writer.Write(Files.Count);
                    foreach (var file in Files)
                    {
                        writer.Write(file.Key);
                        writer.Write(file.Value.Length);
                        writer.Write(file.Value);
                    }
                    writer.Write(GameStateFamily);
                    writer.Write(DowntownSimGUID);
                    writer.Write(LotTransitInfo);
                    writer.Write(ActiveMembers.Count);
                    foreach (var family in ActiveMembers)
                    {
                        writer.Write(family.Key);
                        writer.Write(family.Value.Length);
                        foreach (var guid in family.Value) writer.Write(guid);
                    }
                }
                return output.ToArray();
            }
        }

        public static CoopWorld Unpack(byte[] data)
        {
            var world = new CoopWorld();
            using (var zip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            using (var reader = new BinaryReader(zip))
            {
                var count = reader.ReadInt32();
                if (count < 0 || count > MaxFiles) throw new InvalidDataException("Bad file count " + count);
                long total = 0;
                for (int i = 0; i < count; i++)
                {
                    var path = reader.ReadString();
                    if (!IsAllowedPath(path)) throw new InvalidDataException("Unexpected file " + path);
                    var length = reader.ReadInt32();
                    total += length;
                    if (length < 0 || total > MaxUnpackedSize) throw new InvalidDataException("Neighbourhood too large");
                    var bytes = reader.ReadBytes(length);
                    if (bytes.Length != length) throw new EndOfStreamException();
                    world.Files[path] = bytes;
                }
                world.GameStateFamily = reader.ReadUInt16();
                world.DowntownSimGUID = reader.ReadUInt32();
                world.LotTransitInfo = reader.ReadInt16();
                var families = reader.ReadInt32();
                if (families < 0 || families > 1024) throw new InvalidDataException("Bad family count " + families);
                for (int f = 0; f < families; f++)
                {
                    var id = reader.ReadUInt16();
                    var members = reader.ReadInt32();
                    if (members < 0 || members > 64) throw new InvalidDataException("Bad family size " + members);
                    var guids = new uint[members];
                    for (int i = 0; i < members; i++) guids[i] = reader.ReadUInt32();
                    world.ActiveMembers[id] = guids;
                }
            }
            if (!world.Files.ContainsKey("Neighborhood.iff") || StaticFiles.Any(x => !world.Files.ContainsKey(x)))
                throw new InvalidDataException("The neighbourhood is incomplete");
            return world;
        }

        /// <summary>
        /// Guests write these files to disk, so only the known neighbourhood files and plainly named
        /// character files are accepted - nothing that could land outside the scratch folder.
        /// </summary>
        public static bool IsAllowedPath(string path)
        {
            if (path == "Neighborhood.iff" || StaticFiles.Contains(path)) return true;
            const string prefix = "Characters/";
            if (!path.StartsWith(prefix) || !path.EndsWith(".iff")) return false;
            var name = path.Substring(prefix.Length, path.Length - prefix.Length - 4);
            if (name.Length == 0 || name.Length > 64) return false;
            return name.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == ' ');
        }

        #endregion

        #region Host

        private struct CachedFile
        {
            public DateTime Written;
            public long Length;
            public byte[] Data;
        }
        private static Dictionary<string, CachedFile> DiskCache = new Dictionary<string, CachedFile>();

        private static byte[] ReadCached(string path)
        {
            var info = new FileInfo(path);
            if (DiskCache.TryGetValue(path, out var cached) && cached.Written == info.LastWriteTimeUtc && cached.Length == info.Length)
                return cached.Data;
            var data = File.ReadAllBytes(path);
            DiskCache[path] = new CachedFile { Written = info.LastWriteTimeUtc, Length = info.Length, Data = data };
            return data;
        }

        /// <summary>
        /// Captures the host's neighbourhood as it is in memory right now (which is ahead of what is saved on disk).
        /// </summary>
        public static CoopWorld Capture(VM vm)
        {
            var content = Content.Get();
            var neigh = content.Neighborhood;
            var world = new CoopWorld();

            using (var stream = new MemoryStream())
            {
                neigh.MainResource.Write(stream);
                world.Files["Neighborhood.iff"] = stream.ToArray();
            }
            foreach (var name in StaticFiles) world.Files[name] = ReadCached(Path.Combine(neigh.UserPath, name));

            var charDir = Path.Combine(neigh.UserPath, "Characters/");
            foreach (var filename in Directory.EnumerateFiles(charDir, "*.iff"))
            {
                var path = "Characters/" + Path.GetFileName(filename);
                if (IsAllowedPath(path)) world.Files[path] = ReadCached(filename);
            }
            //Sims changed since the last save only exist in memory
            foreach (var guid in neigh.DirtyAvatars)
            {
                var obj = content.WorldObjects.Get(guid);
                if (obj?.Resource?.Name == null) continue;
                var path = "Characters/" + Path.GetFileName(obj.Resource.Name);
                if (!IsAllowedPath(path)) continue;
                using (var stream = new MemoryStream())
                {
                    obj.Resource.MainIff.Write(stream);
                    world.Files[path] = stream.ToArray();
                }
            }

            world.GameStateFamily = (ushort)(neigh.GameState.ActiveFamily?.ChunkID ?? NoFamily);
            world.DowntownSimGUID = neigh.GameState.DowntownSimGUID;
            world.LotTransitInfo = neigh.GameState.LotTransitInfo;

            foreach (var family in neigh.MainResource.List<FAMI>() ?? new List<FAMI>())
            {
                if (family.RuntimeSubset != null && family.RuntimeSubset.Length > 0)
                    world.ActiveMembers[family.ChunkID] = family.RuntimeSubset;
            }
            return world;
        }

        #endregion

        #region Guest

        /// <summary>True while this game has the host's neighbourhood loaded instead of its own.</summary>
        public static bool Applied { get; private set; }
        private static CoopWorld Current;
        private static FSO.Content.TS1.TS1GameState OwnGameState; //the guest's own, put back by Restore

        /// <summary>
        /// Loads this neighbourhood in place of the guest's own. Called just before the lot state it belongs to is loaded.
        /// </summary>
        public void Apply()
        {
            var root = Path.Combine(FSOEnvironment.UserDir, "UserData" + (GuestSlot + 1) + "/");
            Directory.CreateDirectory(Path.Combine(root, "Houses"));
            var charDir = Path.Combine(root, "Characters");
            Directory.CreateDirectory(charDir);

            foreach (var file in Files)
            {
                var path = Path.Combine(root, file.Key);
                if (File.Exists(path) && new FileInfo(path).Length == file.Value.Length
                    && File.ReadAllBytes(path).AsSpan().SequenceEqual(file.Value)) continue; //unchanged since the last state
                File.WriteAllBytes(path, file.Value);
            }
            foreach (var existing in Directory.GetFiles(charDir))
            {
                if (!Files.ContainsKey("Characters/" + Path.GetFileName(existing))) File.Delete(existing);
            }

            //the lot pickers (downtown, vacation...) read house files for their pictures and names. The host's
            //are not sent, so stand in this game's own; nothing in the simulation depends on them.
            var ownHouses = Path.Combine(FSOEnvironment.UserDir, "UserData/Houses");
            if (Directory.Exists(ownHouses))
            {
                foreach (var house in Directory.GetFiles(ownHouses))
                {
                    var target = Path.Combine(root, "Houses", Path.GetFileName(house));
                    if (!File.Exists(target)) File.Copy(house, target);
                }
            }

            var neigh = Content.Get().Neighborhood;
            if (!Applied)
            {
                OwnGameState = neigh.GameState;
                neigh.GameState = new FSO.Content.TS1.TS1GameState();
            }
            Applied = true; //from here on our own neighbourhood is no longer the one in memory
            neigh.InitSpecific(GuestSlot);
            neigh.GameState.ActiveFamily = (GameStateFamily == NoFamily) ? null : neigh.GetFamily(GameStateFamily);
            neigh.GameState.DowntownSimGUID = DowntownSimGUID;
            neigh.GameState.LotTransitInfo = LotTransitInfo;
            foreach (var active in ActiveMembers)
            {
                var family = neigh.GetFamily(active.Key);
                if (family != null) family.RuntimeSubset = active.Value;
            }
            Current = this;
        }

        /// <summary>
        /// The family object a guest's lot should use, given the copy that came with a lot state: the one from
        /// the host's neighbourhood when we have it (as on the host), with the host's active members.
        /// Loading a lot state keeps whichever family the VM already has, so this is set before loading.
        /// </summary>
        public static FAMI ResolveFamily(FAMI lotFamily)
        {
            if (lotFamily == null) return null;

            if (Applied && Current != null)
            {
                var family = Content.Get().Neighborhood.GetFamily(lotFamily.ChunkID);
                if (family != null)
                {
                    //its active members were set from the host's when the neighbourhood was loaded
                    if (family.RuntimeSubset == null || family.RuntimeSubset.Length == 0) family.SelectWholeFamily();
                    return family;
                }
            }
            lotFamily.SelectWholeFamily(); //the active members are not part of the lot state
            return lotFamily;
        }

        /// <summary>
        /// Loads the guest's own neighbourhood again, as it was last saved.
        /// </summary>
        public static void Restore()
        {
            if (!Applied) return;
            Applied = false;
            Current = null;
            var neigh = Content.Get().Neighborhood;
            neigh.InitSpecific(0);
            if (OwnGameState != null)
            {
                //the family object it points at was reloaded with the neighbourhood
                var family = OwnGameState.ActiveFamily;
                OwnGameState.ActiveFamily = (family == null) ? null : neigh.GetFamily(family.ChunkID);
                neigh.GameState = OwnGameState;
                OwnGameState = null;
            }
        }

        #endregion
    }
}
