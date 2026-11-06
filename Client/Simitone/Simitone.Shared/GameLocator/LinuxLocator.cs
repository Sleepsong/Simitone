using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Simitone.Windows.GameLocator
{
    public class LinuxLocator : ILocator
    {
        /// <summary>The Sims: Legacy Collection's Steam app ID.</summary>
        public const int LegacyCollectionAppId = 3314060;
        /// <summary>The folder names the Legacy Collection has been seen installed under.</summary>
        private static readonly string[] SteamFolderNames = { "The Sims Legacy Collection", "The Sims" };

        public string FindTheSimsOnline()
        {
            return "";
            //string localDir = @"../The Sims Online/TSOClient/";
            //if (File.Exists(Path.Combine(localDir, "tuning.dat"))) return localDir;

            //return "game/TSOClient/";
        }

        public string FindTheSims1()
        {
            // Return first found installation
            var installations = GetAllTheSims1Installations();
            return installations.Count > 0 ? installations[0].path : null;
        }

        /// <summary>
        /// Gets all detected The Sims 1 installations on Linux
        /// </summary>
        public List<(string description, string path, TS1InstallationType type)> GetAllTheSims1Installations()
        {
            var installations = new List<(string, string, TS1InstallationType)>();
            var seen = new HashSet<string>();
            void Add(string description, string path, TS1InstallationType type)
            {
                // (the same folder reached two ways, e.g. ~/.steam/steam and ~/.local/share/Steam, counts once)
                if (seen.Add(RealPath(path))) installations.Add((description, path, type));
            }

            // Check relative path (portable install)
            string localDir = @"../The Sims/";
            if (HasGameData(localDir))
            {
                Add("Portable Install (Relative Directory)", localDir, TS1InstallationType.Portable);
            }

            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Steam (Proton/Steam Play): every Steam library, the folder the Legacy Collection's manifest names, or the
            // usual folder names
            foreach (var steamPath in SteamGameFolders(homeDir))
            {
                Add("Steam - The Sims: Legacy Collection (Proton)", steamPath, TS1InstallationType.Steam);
            }

            // Check Wine (default prefix)
            var winePath = Path.Combine(homeDir, ".wine/drive_c/Program Files/Maxis/The Sims/");
            if (HasGameData(winePath))
            {
                Add("Wine - Default Prefix", winePath, TS1InstallationType.Wine);
            }

            // Check Wine (x86 prefix)
            var winePath32 = Path.Combine(homeDir, ".wine/drive_c/Program Files (x86)/Maxis/The Sims/");
            if (HasGameData(winePath32))
            {
                Add("Wine - Default Prefix (x86)", winePath32, TS1InstallationType.Wine);
            }

            // Check fallback location
            if (HasGameData("game1/"))
            {
                Add("Fallback Location", "game1/", TS1InstallationType.Unknown);
            }

            return installations;
        }

        /// <summary>A folder The Sims' game files are in (it has GameData/Behavior.iff).</summary>
        public static bool HasGameData(string folder) => File.Exists(Path.Combine(folder, "GameData", "Behavior.iff"));

        // ------------------------------------------------------------------ Steam

        /// <summary>
        /// Where Steam can be: the native client's folders (~/.steam/steam, ~/.steam/root, ~/.local/share/Steam,
        /// SteamOS's included) and the Flatpak's. Only those that exist.
        /// </summary>
        public static IEnumerable<string> SteamRoots(string home)
        {
            var roots = new[]
            {
                ".steam/steam", ".steam/root", ".local/share/Steam",
                ".var/app/com.valvesoftware.Steam/.local/share/Steam", ".var/app/com.valvesoftware.Steam/data/Steam"
            };
            return roots.Select(r => Path.Combine(home, r)).Where(Directory.Exists);
        }

        /// <summary>
        /// Every Steam library folder: each Steam root and the libraries its libraryfolders.vdf lists (other drives,
        /// SD cards). Each once.
        /// </summary>
        public static List<string> SteamLibraries(string home)
        {
            var libraries = new List<string>();
            var seen = new HashSet<string>();
            void Add(string path)
            {
                if (Directory.Exists(path) && seen.Add(RealPath(path))) libraries.Add(path);
            }
            foreach (var root in SteamRoots(home))
            {
                Add(root);
                foreach (var vdf in new[] { Path.Combine(root, "steamapps", "libraryfolders.vdf"), Path.Combine(root, "config", "libraryfolders.vdf") })
                {
                    if (!File.Exists(vdf)) continue;
                    try
                    {
                        foreach (var line in File.ReadLines(vdf))
                        {
                            var match = LibraryPath.Match(line);
                            if (match.Success) Add(match.Groups["path"].Value.Replace(@"\\", @"\"));
                        }
                    }
                    catch (IOException) { } // (locked or unreadable: the root itself still counts)
                    catch (UnauthorizedAccessException) { }
                }
            }
            return libraries;
        }

        /// <summary>
        /// The Legacy Collection's game folders in every Steam library: the folder its app manifest names, then the
        /// usual names, under steamapps/common (Steam's layout) or common. A folder counts if it has The Sims'
        /// GameData, or one of its own folders does.
        /// </summary>
        public static List<string> SteamGameFolders(string home)
        {
            var found = new List<string>();
            var seen = new HashSet<string>();
            foreach (var library in SteamLibraries(home))
            {
                var names = new List<string>();
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{LegacyCollectionAppId}.acf");
                var installDir = InstallDir(manifest);
                if (installDir != null) names.Add(installDir);
                names.AddRange(SteamFolderNames);
                foreach (var common in new[] { Path.Combine(library, "steamapps", "common"), Path.Combine(library, "common") })
                {
                    foreach (var name in names)
                    {
                        var game = GameFolderIn(Path.Combine(common, name));
                        if (game != null && seen.Add(RealPath(game))) found.Add(game);
                    }
                }
            }
            return found;
        }

        /// <summary>The folder with The Sims' GameData: this one, or one directly inside it. Null if neither.</summary>
        private static string GameFolderIn(string folder)
        {
            if (!Directory.Exists(folder)) return null;
            if (HasGameData(folder)) return WithSlash(folder);
            try
            {
                foreach (var sub in Directory.GetDirectories(folder))
                    if (HasGameData(sub)) return WithSlash(sub);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        private static string InstallDir(string manifest)
        {
            if (!File.Exists(manifest)) return null;
            try
            {
                foreach (var line in File.ReadLines(manifest))
                {
                    var match = InstallDirLine.Match(line);
                    if (match.Success) return match.Groups["dir"].Value;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        private static readonly Regex LibraryPath = new Regex("\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex InstallDirLine = new Regex("\"installdir\"\\s+\"(?<dir>[^\"]+)\"", RegexOptions.Compiled);

        private static string WithSlash(string path) => path.EndsWith("/") ? path : path + "/";

        /// <summary>A path with its symbolic links followed (~/.steam/steam is usually a link to ~/.local/share/Steam).</summary>
        public static string RealPath(string path)
        {
            try
            {
                var current = "/";
                foreach (var part in Path.GetFullPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    var next = Path.Combine(current, part);
                    for (int hops = 0; hops < 32; hops++)
                    {
                        var target = new DirectoryInfo(next).LinkTarget ?? (File.Exists(next) ? new FileInfo(next).LinkTarget : null);
                        if (target == null) break;
                        next = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(current, target));
                    }
                    current = next;
                }
                return current.TrimEnd('/');
            }
            catch (Exception)
            {
                return Path.GetFullPath(path).TrimEnd('/');
            }
        }

        // ------------------------------------------------------------------ saves

        /// <summary>
        /// The Legacy Collection keeps its saves (the neighbourhood: UserData, UserData2...) in its Proton prefix, as
        /// on Windows: steamapps/compatdata/3314060/pfx/drive_c/users/steamuser/Saved Games/Electronic Arts/The Sims 25.
        /// This finds that folder, in the game's own library first, then in every library. Null if there's none (the
        /// game hasn't been run yet, or isn't the Steam one).
        /// </summary>
        public static string FindProtonSaves(string home, string gamePath)
        {
            var libraries = new List<string>();
            // the game's library: the folder above its steamapps/common, or above common
            var dir = new DirectoryInfo(Path.GetFullPath(gamePath));
            for (var d = dir; d != null; d = d.Parent)
            {
                if (d.Name == "common" && d.Parent != null)
                {
                    libraries.Add(d.Parent.Name == "steamapps" && d.Parent.Parent != null ? d.Parent.Parent.FullName : d.Parent.FullName);
                    break;
                }
            }
            libraries.AddRange(SteamLibraries(home));
            const string inPrefix = "pfx/drive_c/users/steamuser/Saved Games/Electronic Arts/The Sims 25";
            foreach (var library in libraries)
            {
                foreach (var compat in new[] { Path.Combine(library, "steamapps", "compatdata"), Path.Combine(library, "compatdata") })
                {
                    var saves = Path.Combine(compat, LegacyCollectionAppId.ToString(), inPrefix);
                    if (Directory.Exists(Path.Combine(saves, "UserData"))) return saves;
                }
            }
            return null;
        }

        /// <summary>
        /// Before the game starts: Simitone plays on its own copy of the neighbourhood (UserData in its user folder),
        /// made the first time from the game's. For the Legacy Collection under Proton the game's is in its prefix,
        /// not the game folder: copy it from there (every UserData folder), unless Simitone already has its own.
        /// Returns what it did, for the log.
        /// </summary>
        public static string PrepareSaves(string home, string gamePath, string userDir)
        {
            if (Directory.Exists(Path.Combine(userDir, "UserData"))) return "neighbourhood: Simitone's own (" + Path.Combine(userDir, "UserData") + ")";
            var saves = FindProtonSaves(home, gamePath);
            if (saves == null)
            {
                return Directory.Exists(Path.Combine(gamePath, "UserData"))
                    ? "neighbourhood: copied from the game folder on first load"
                    : "neighbourhood: NOT FOUND. Run The Sims Legacy Collection once so it makes its saves, or copy its "
                      + "UserData folder to " + Path.Combine(userDir, "UserData");
            }
            var copied = new List<string>();
            foreach (var source in Directory.GetDirectories(saves, "UserData*"))
            {
                var destination = Path.Combine(userDir, Path.GetFileName(source));
                if (Directory.Exists(destination)) continue;
                // (all or nothing: a half-copied neighbourhood would be used as if it were whole)
                var partial = destination + ".copying";
                if (Directory.Exists(partial)) Directory.Delete(partial, true);
                CopyFolder(source, partial);
                Directory.Move(partial, destination);
                copied.Add(Path.GetFileName(source));
            }
            return $"neighbourhood: copied {string.Join(", ", copied)} from the Legacy Collection's saves ({saves})";
        }

        private static void CopyFolder(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
            foreach (var folder in Directory.GetDirectories(source))
                CopyFolder(folder, Path.Combine(destination, Path.GetFileName(folder)));
        }
    }
}
