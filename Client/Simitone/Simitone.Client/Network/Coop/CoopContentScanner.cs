using FSO.Common;
using FSO.Content;
using FSO.Content.Framework;
using FSO.Content.TS1;
using FSO.Files.FAR1;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// The link between co-op content sharing and this game's own object catalogue: lists the object files
    /// the game is using, and on a guest swaps in the host's copies for the length of a session.
    /// </summary>
    public static class CoopContentScanner
    {
        public static string CacheRoot { get { return Path.Combine(FSOEnvironment.UserDir, "CoopCache"); } }

        /// <summary>Where a guest keeps the object files it has been sent, for this and later sessions.</summary>
        public static string ObjectCacheDir { get { return Path.Combine(CacheRoot, "Objects"); } }

        //an archive only says which file it is to itself
        private static readonly FieldInfo ArchivePathField = typeof(FAR1Archive).GetField("m_Path", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Lists and fingerprints every object file this game uses. Call on the game thread; the listing is done
        /// before it returns and the fingerprinting, which reads the files, carries on in the background.
        /// </summary>
        public static Task<CoopManifest> BuildLocalAsync()
        {
            CoopManifest manifest;
            try
            {
                manifest = ListLocalFiles();
            }
            catch (Exception e)
            {
                //better that co-op says it cannot compare objects than that the game stops here
                Console.WriteLine("COOP could not list this game's object files: " + e);
                return Task.FromException<CoopManifest>(e);
            }
            return Task.Run(() =>
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var read = new CoopFingerprints(Path.Combine(CacheRoot, "fingerprints.tsv")).Fill(manifest);
                Console.WriteLine("COOP checked " + manifest.Files.Count + " object files (" + read + " read) in " + timer.ElapsedMilliseconds + "ms");
                return manifest;
            });
        }

        private static CoopManifest ListLocalFiles()
        {
            var content = Content.Get();
            var objects = content.WorldObjects;

            //the archive entry behind each object file name, chosen the same way the game chooses it
            var archiveEntries = new Dictionary<string, Far1ProviderEntry<object>>();
            var iffs = new TS1SubProvider<IffFile>(content.TS1Global, ".iff");
            iffs.Init();
            foreach (var item in iffs.ListGeneric())
            {
                var entry = item as Far1ProviderEntry<object>;
                if (entry != null) archiveEntries[BaseName(entry.FarEntry.Filename).ToLowerInvariant()] = entry;
            }

            var files = new Dictionary<string, CoopContentFile>();
            var guids = new Dictionary<CoopContentFile, List<uint>>();
            var skipped = 0;
            lock (objects.Entries)
            {
                foreach (var pair in objects.Entries)
                {
                    var reference = pair.Value;
                    var key = (int)reference.Source + "|" + reference.FileName;
                    if (!files.TryGetValue(key, out var file))
                    {
                        file = Describe(reference, archiveEntries);
                        files[key] = file;
                        if (file != null) guids[file] = new List<uint>();
                    }
                    if (file != null) guids[file].Add((uint)pair.Key);
                    else if (reference.Source == GameObjectSource.Far || reference.Source == GameObjectSource.Standalone) skipped++;
                }
            }
            if (skipped > 0) Console.WriteLine("COOP " + skipped + " objects could not be traced to a file and will not be compared");

            var manifest = new CoopManifest();
            foreach (var pair in guids)
            {
                pair.Key.Guids = pair.Value.ToArray();
                manifest.Files.Add(pair.Key);
            }
            return manifest;
        }

        /// <summary>
        /// Where the file behind an object is, or null for objects that are not compared: user made Sims
        /// (the host sends those with its neighbourhood) and objects added by Simitone's own patches.
        /// </summary>
        private static CoopContentFile Describe(GameObjectReference reference, Dictionary<string, Far1ProviderEntry<object>> archiveEntries)
        {
            try
            {
                if (reference.Source == GameObjectSource.Standalone)
                {
                    //a loose file, which the game only reads from Downloads
                    var info = new FileInfo(reference.FileName);
                    if (!info.Exists || info.Length > int.MaxValue) return null;
                    return new CoopContentFile
                    {
                        Name = Path.GetFileName(reference.FileName),
                        Download = true,
                        Size = (int)info.Length,
                        Path = reference.FileName
                    };
                }
                if (reference.Source == GameObjectSource.Far)
                {
                    if (!archiveEntries.TryGetValue((reference.FileName ?? "").ToLowerInvariant(), out var entry)) return null;
                    var archivePath = ArchivePathField?.GetValue(entry.Archive) as string;
                    if (archivePath == null) return null;
                    return new CoopContentFile
                    {
                        Name = BaseName(entry.FarEntry.Filename),
                        Download = archivePath.Replace('\\', '/').IndexOf("/downloads/", StringComparison.OrdinalIgnoreCase) >= 0,
                        Size = entry.FarEntry.DataLength,
                        Path = archivePath,
                        Offset = entry.FarEntry.DataOffset
                    };
                }
            }
            catch (Exception) { }
            return null;
        }

        private static string BaseName(string filename)
        {
            return Path.GetFileName(filename.Replace('\\', '/'));
        }

        #region Guest

        //what each object we replaced pointed at before (null: this game did not have it)
        private static Dictionary<uint, GameObjectReference> Replaced = new Dictionary<uint, GameObjectReference>();

        /// <summary>
        /// Makes this game take the given objects from the host's copies of their files (already saved at the
        /// given paths) instead of its own, until <see cref="Restore"/>. They can be used on a lot straight away,
        /// but are not added to this game's buy catalogue.
        /// </summary>
        public static void Apply(List<KeyValuePair<CoopContentFile, string>> files)
        {
            var objects = Content.Get().WorldObjects;
            var people = (objects as TS1ObjectProvider)?.PersonGUIDs;
            foreach (var pair in files)
            {
                //a file can define objects the host takes from elsewhere, so only the ones it listed
                var wanted = new HashSet<uint>(pair.Key.Guids);
                var iff = new IffFile(pair.Value);
                iff.MarkThrowaway();
                var definitions = iff.List<OBJD>();
                if (definitions == null) continue;
                foreach (var objd in definitions)
                {
                    var guid = objd.GUID;
                    if (!wanted.Contains(guid)) continue;
                    lock (objects.Entries)
                    {
                        if (!Replaced.ContainsKey(guid))
                        {
                            objects.Entries.TryGetValue(guid, out var original);
                            Replaced[guid] = original;
                        }
                    }
                    objects.RemoveObject(guid); //also forgets our own copy if it was loaded
                    lock (objects.Entries)
                    {
                        objects.Entries[guid] = new GameObjectReference(objects)
                        {
                            FileName = pair.Value,
                            ID = guid,
                            Name = objd.ChunkLabel,
                            Source = GameObjectSource.Standalone,
                            Group = (short)objd.MasterID,
                            SubIndex = objd.SubIndex,
                            GlobalSimObject = objd.Global == 1 && objd.ObjectType != OBJDType.GiftToken
                        };
                    }
                    if (objd.ObjectType == OBJDType.Person) people?.Add(guid);
                }
            }
            if (Replaced.Count > 0) Console.WriteLine("COOP using the host's copy of " + Replaced.Count + " objects for this session");
        }

        /// <summary>
        /// Puts this game's own objects back.
        /// </summary>
        public static void Restore()
        {
            if (Replaced.Count == 0) return;
            var objects = Content.Get().WorldObjects;
            foreach (var pair in Replaced)
            {
                objects.RemoveObject(pair.Key);
                if (pair.Value != null)
                {
                    lock (objects.Entries) objects.Entries[pair.Key] = pair.Value;
                }
            }
            Replaced.Clear();
        }

        #endregion
    }
}
