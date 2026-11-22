using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// One file of a game that defines objects: a loose .iff, or an entry in one of the game's .far archives.
    /// </summary>
    public class CoopContentFile
    {
        public string Name;    //the file's name, without folders. Patches are matched to objects by this
        public bool Download;  //custom content from the Downloads folder, rather than part of the game itself
        public int Size;
        public byte[] Hash;    //fingerprint of the file's bytes (see CoopContent.HashOf)
        public uint[] Guids = new uint[0]; //the objects this game takes from the file

        //where the bytes are on this machine. Not sent.
        public string Path;
        public long Offset;

        public byte[] ReadBytes()
        {
            using (var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return CoopContent.ReadRange(stream, Offset, Size);
            }
        }
    }

    /// <summary>
    /// Every object file of a game, as sent by the host for guests to compare their own against.
    /// </summary>
    public class CoopManifest
    {
        public List<CoopContentFile> Files = new List<CoopContentFile>();

        private const int MaxFiles = 200000;
        private const int MaxGuidsPerFile = 8192;

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
                        writer.Write(file.Name);
                        writer.Write(file.Download);
                        writer.Write(file.Size);
                        writer.Write(file.Hash, 0, CoopContent.HashSize);
                        writer.Write(file.Guids.Length);
                        foreach (var guid in file.Guids) writer.Write(guid);
                    }
                }
                return output.ToArray();
            }
        }

        public static CoopManifest Unpack(byte[] data)
        {
            var manifest = new CoopManifest();
            using (var zip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            using (var reader = new BinaryReader(zip))
            {
                var count = reader.ReadInt32();
                if (count < 0 || count > MaxFiles) throw new InvalidDataException("Bad file count " + count);
                for (int i = 0; i < count; i++)
                {
                    var file = new CoopContentFile();
                    file.Name = reader.ReadString();
                    file.Download = reader.ReadBoolean();
                    file.Size = reader.ReadInt32();
                    file.Hash = reader.ReadBytes(CoopContent.HashSize);
                    if (file.Size < 0 || file.Hash.Length != CoopContent.HashSize) throw new InvalidDataException("Bad file entry");
                    var guids = reader.ReadInt32();
                    if (guids < 0 || guids > MaxGuidsPerFile) throw new InvalidDataException("Bad object count " + guids);
                    file.Guids = new uint[guids];
                    for (int j = 0; j < guids; j++) file.Guids[j] = reader.ReadUInt32();
                    manifest.Files.Add(file);
                }
            }
            return manifest;
        }
    }

    /// <summary>
    /// What a guest has to do before it can play on a host: nothing, fetch some files, or give up.
    /// </summary>
    public class CoopContentPlan
    {
        /// <summary>Why this game cannot join, or null if it can.</summary>
        public string Problem;
        /// <summary>Indices into the host's manifest of the files this game must use in place of its own.</summary>
        public List<int> Needed = new List<int>();
    }

    public static class CoopContent
    {
        public const int HashSize = 16;
        /// <summary>The largest object file that is sent. Frames are held in memory whole, and are capped at 64MB.</summary>
        public const int MaxFileSize = 48 * 1024 * 1024;

        public static byte[] HashOf(byte[] data, int offset, int count)
        {
            var full = SHA256.HashData(new ReadOnlySpan<byte>(data, offset, count));
            var hash = new byte[HashSize];
            Buffer.BlockCopy(full, 0, hash, 0, HashSize);
            return hash;
        }

        public static bool HashEquals(byte[] a, byte[] b)
        {
            return a != null && b != null && a.AsSpan().SequenceEqual(b);
        }

        public static byte[] ReadRange(Stream stream, long offset, int count)
        {
            var data = new byte[count];
            stream.Seek(offset, SeekOrigin.Begin);
            var read = 0;
            while (read < count)
            {
                var got = stream.Read(data, read, count - read);
                if (got <= 0) throw new EndOfStreamException();
                read += got;
            }
            return data;
        }

        /// <summary>
        /// Works out which of the host's object files a guest needs. The aim is that every object the host has
        /// runs from identical bytes on both sides:
        ///  - a host file the guest has an identical copy of is fine, wherever each side keeps it;
        ///  - custom content the guest lacks, or has a different version of, is fetched;
        ///  - a built-in object the guest has a different copy of (it has its own download replacing it, or
        ///    the two games load their archives in a different order) is fetched too;
        ///  - built-in objects the guest lacks altogether mean an expansion pack is missing. Objects alone would
        ///    not make that work, so the guest cannot join.
        /// Objects only the guest has do not matter.
        /// </summary>
        public static CoopContentPlan Compare(CoopManifest host, CoopManifest local)
        {
            var localByGuid = new Dictionary<uint, CoopContentFile>();
            foreach (var file in local.Files)
            {
                foreach (var guid in file.Guids) localByGuid[guid] = file;
            }

            var plan = new CoopContentPlan();
            var missingGame = new List<string>();
            var tooLarge = new List<string>();
            for (int i = 0; i < host.Files.Count; i++)
            {
                var file = host.Files[i];
                if (file.Guids.Length == 0) continue;

                var present = 0;
                var identical = true;
                foreach (var guid in file.Guids)
                {
                    if (localByGuid.TryGetValue(guid, out var mine))
                    {
                        present++;
                        if (!HashEquals(mine.Hash, file.Hash)) identical = false;
                    }
                    else identical = false;
                }
                if (identical) continue;

                if (!file.Download && present == 0) missingGame.Add(file.Name);
                else if (file.Size > MaxFileSize) tooLarge.Add(file.Name);
                else plan.Needed.Add(i);
            }

            if (missingGame.Count > 0)
            {
                plan.Problem = "This game is missing " + missingGame.Count + " of the built-in object files in the host's game (" +
                    Sample(missingGame) + "). That usually means the host has an expansion pack this game does not. " +
                    "Both players need the same expansion packs.";
            }
            else if (tooLarge.Count > 0)
            {
                plan.Problem = tooLarge.Count + " of the host's object files " + (tooLarge.Count == 1 ? "is" : "are") +
                    " too large to send (" + Sample(tooLarge) + "). Install " + (tooLarge.Count == 1 ? "it" : "them") + " in this game to join.";
            }
            return plan;
        }

        private static string Sample(List<string> names)
        {
            return string.Join(", ", names.Take(3)) + (names.Count > 3 ? "..." : "");
        }

        /// <summary>
        /// Where a guest keeps its copy of one of a host's files: a folder named after the file's fingerprint,
        /// so the same file is only ever fetched once, holding the file under its own name.
        /// </summary>
        public static string CachePath(string cacheDir, CoopContentFile file)
        {
            return System.IO.Path.Combine(cacheDir, Convert.ToHexString(file.Hash).ToLowerInvariant(), SafeName(file.Name));
        }

        /// <summary>
        /// The host chooses the name, so anything that could leave the folder or upset a file system is replaced.
        /// </summary>
        public static string SafeName(string name)
        {
            var chars = (name ?? "").ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (c < 32 || c == 127 || "/\\:*?\"<>|".IndexOf(c) >= 0) chars[i] = '_';
            }
            var safe = new string(chars).Trim(' ', '.');
            if (safe.Length > 120) safe = safe.Substring(safe.Length - 120).TrimStart(' ', '.');
            if (safe == "") safe = "object.iff";
            return safe;
        }
    }

    /// <summary>
    /// Fingerprints object files, remembering the result between runs. Fingerprinting a whole game means
    /// reading every object in it, which takes a few seconds; after that only changed files are read again.
    /// </summary>
    public class CoopFingerprints
    {
        private readonly string CacheFile;

        public CoopFingerprints(string cacheFile)
        {
            CacheFile = cacheFile;
        }

        /// <summary>
        /// Sets the Hash of every file in the manifest. Files that cannot be read are dropped from it.
        /// Returns how many files had to be read.
        /// </summary>
        public int Fill(CoopManifest manifest)
        {
            var known = Load();
            var computed = 0;
            var changed = false;
            var result = new Dictionary<string, string>();

            foreach (var group in manifest.Files.GroupBy(x => x.Path))
            {
                try
                {
                    var info = new FileInfo(group.Key);
                    //a remembered fingerprint counts while the file it was read from has not changed
                    var stamp = info.Length + "\t" + info.LastWriteTimeUtc.Ticks;
                    FileStream stream = null;
                    try
                    {
                        foreach (var file in group.OrderBy(x => x.Offset))
                        {
                            var key = file.Offset + "\t" + file.Size + "\t" + stamp + "\t" + group.Key;
                            if (known.TryGetValue(key, out var hex) && hex.Length == CoopContent.HashSize * 2)
                            {
                                file.Hash = Convert.FromHexString(hex);
                            }
                            else
                            {
                                if (stream == null) stream = new FileStream(group.Key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                var data = CoopContent.ReadRange(stream, file.Offset, file.Size);
                                file.Hash = CoopContent.HashOf(data, 0, data.Length);
                                hex = Convert.ToHexString(file.Hash);
                                computed++;
                                changed = true;
                            }
                            result[key] = hex;
                        }
                    }
                    finally
                    {
                        stream?.Dispose();
                    }
                }
                catch (Exception)
                {
                    foreach (var file in group) file.Hash = null;
                }
            }

            manifest.Files.RemoveAll(x => x.Hash == null);
            if (changed || result.Count != known.Count) Save(result);
            return computed;
        }

        private Dictionary<string, string> Load()
        {
            var known = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(CacheFile)) return known;
                foreach (var line in File.ReadAllLines(CacheFile))
                {
                    var split = line.IndexOf('\t');
                    if (split > 0) known[line.Substring(split + 1)] = line.Substring(0, split);
                }
            }
            catch (Exception) { } //then everything is read again
            return known;
        }

        private void Save(Dictionary<string, string> entries)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CacheFile));
                var temp = CacheFile + ".tmp";
                File.WriteAllLines(temp, entries.Select(x => x.Value + "\t" + x.Key));
                File.Move(temp, CacheFile, true);
            }
            catch (Exception) { } //only costs time next run
        }
    }
}
