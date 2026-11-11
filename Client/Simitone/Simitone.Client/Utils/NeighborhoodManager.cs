using FSO.Common;
using FSO.Content;
using FSO.Content.Model;
using FSO.Content.TS1;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Simitone.Client.UI.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// A neighbourhood the player can switch to: either a save slot that already exists in the Simitone
    /// user directory, or one of the stock slots in the TS1 install that has not been imported yet.
    /// </summary>
    public class NeighborhoodSlot
    {
        /// <summary>The id passed to TS1NeighborhoodProvider.InitSpecific. 0 is "UserData", n is "UserData(n+1)".</summary>
        public int ID;
        public string Name;
        public string Author;
        public string Description;
        /// <summary>True if the slot is already in the Simitone user directory. False if it would be imported from TS1.</summary>
        public bool Imported;
        /// <summary>True if this slot was created by Simitone (from a template or a package), rather than imported from TS1.</summary>
        public bool Custom;
    }

    /// <summary>
    /// A neighbourhood package (mod) found in the Neighborhoods folder of the Simitone user directory.
    /// </summary>
    public class NeighborhoodPackage
    {
        public string Path;
        public NeighborhoodManifest Manifest;

        public string Name => string.IsNullOrWhiteSpace(Manifest.Name) ? System.IO.Path.GetFileName(Path) : Manifest.Name;
    }

    /// <summary>
    /// neighborhood.json. In a package this describes the mod; once installed, a copy lives in the save slot
    /// so the slot keeps its name and custom map art.
    /// </summary>
    public class NeighborhoodManifest
    {
        [JsonProperty("name")]
        public string Name;
        [JsonProperty("author")]
        public string Author;
        [JsonProperty("description")]
        public string Description;
        /// <summary>
        /// Map screen overrides, keyed by area number:
        /// 1 = original neighbourhood screen, 2 = Downtown, 3 = Vacation Island, 4 = main neighbourhood (Unleashed),
        /// 5 = Studio Town, 7 = Magic Town.
        /// </summary>
        [JsonProperty("areas")]
        public Dictionary<string, NeighborhoodAreaManifest> Areas;
    }

    public class NeighborhoodAreaManifest
    {
        /// <summary>Background image. A file in the package (png or bmp), or a TS1 content path.</summary>
        [JsonProperty("graphic")]
        public string Graphic;
        /// <summary>How much house thumbnails are scaled down by. Stock screens use 1, the Unleashed one uses 2.</summary>
        [JsonProperty("scale")]
        public float? Scale;
        /// <summary>HIT event for the music.</summary>
        [JsonProperty("music")]
        public string Music;
        /// <summary>HIT event for a looping background sound.</summary>
        [JsonProperty("ambience")]
        public string Ambience;
        /// <summary>If true animations play forwards then backwards, otherwise they loop.</summary>
        [JsonProperty("pulsate")]
        public bool? Pulsate;
        /// <summary>Frames (at 60fps) each animation frame is shown for.</summary>
        [JsonProperty("frameDuration")]
        public int? FrameDuration;
        [JsonProperty("animations")]
        public List<NeighborhoodAnimationManifest> Animations;
    }

    public class NeighborhoodAnimationManifest
    {
        [JsonProperty("x")]
        public float X;
        [JsonProperty("y")]
        public float Y;
        [JsonProperty("frames")]
        public List<string> Frames;
    }

    /// <summary>
    /// Lists, creates, installs and switches between neighbourhoods.
    ///
    /// Neighbourhoods are the "UserData", "UserData2", ... folders in the Simitone user directory, which is what
    /// TS1NeighborhoodProvider.InitSpecific already loads. Custom ones are just more of those folders, with an
    /// optional neighborhood.json for a name and custom map screens.
    /// </summary>
    public static class NeighborhoodManager
    {
        public const string ManifestName = "neighborhood.json";
        public const string PackageFolder = "Neighborhoods";
        private const string ActiveFile = "neighborhoods.json";

        /// <summary>The slot the neighbourhood provider currently has loaded.</summary>
        public static int ActiveSlot { get; private set; }

        private static bool StartupApplied;
        private static NeighborhoodManifest ActiveManifest;
        private static Dictionary<string, ITextureRef> TextureCache = new Dictionary<string, ITextureRef>();

        private class ActiveState
        {
            [JsonProperty("active")]
            public int Active;
        }

        // ---- paths ----

        public static string FolderName(int slot)
        {
            return "UserData" + ((slot == 0) ? "" : (slot + 1).ToString());
        }

        public static string SlotPath(int slot)
        {
            return Path.Combine(FSOEnvironment.UserDir, FolderName(slot));
        }

        public static string PackagesPath => Path.Combine(FSOEnvironment.UserDir, PackageFolder);

        /// <summary>
        /// Where TS1 keeps its own neighbourhoods. InitSpecific imports a slot from here the first time it is used.
        /// </summary>
        private static string TS1SavesPath
        {
            get
            {
                if (Content.TS1SteamInstall)
                {
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Saved Games", "Electronic Arts", "The Sims 25");
                }
                return Content.Get().TS1BasePath;
            }
        }

        private static bool IsNeighborhoodFolder(string path)
        {
            return path != null && Directory.Exists(path) && FindEntry(path, "Neighborhood.iff", false) != null;
        }

        /// <summary>Case insensitive lookup of a direct child, since packages are made on Windows and played anywhere.</summary>
        private static string FindEntry(string dir, string name, bool directory)
        {
            if (!Directory.Exists(dir)) return null;
            var entries = directory ? Directory.GetDirectories(dir) : Directory.GetFiles(dir);
            return entries.FirstOrDefault(x => string.Equals(Path.GetFileName(x), name, StringComparison.OrdinalIgnoreCase));
        }

        private static int? SlotFromFolder(string folder)
        {
            var name = Path.GetFileName(folder.TrimEnd('/', '\\'));
            if (!name.StartsWith("UserData", StringComparison.OrdinalIgnoreCase)) return null;
            var suffix = name.Substring(8);
            if (suffix == "") return 0;
            if (int.TryParse(suffix, out var num) && num >= 2 && suffix == num.ToString()) return num - 1;
            return null;
        }

        private static IEnumerable<int> SlotsIn(string root)
        {
            if (root == null || !Directory.Exists(root)) yield break;
            foreach (var dir in Directory.GetDirectories(root, "UserData*"))
            {
                var slot = SlotFromFolder(dir);
                if (slot != null && IsNeighborhoodFolder(dir)) yield return slot.Value;
            }
        }

        // ---- listing ----

        public static NeighborhoodManifest ReadManifest(string folder)
        {
            try
            {
                var file = FindEntry(folder, ManifestName, false);
                if (file == null) return null;
                return JsonConvert.DeserializeObject<NeighborhoodManifest>(File.ReadAllText(file));
            }
            catch (Exception)
            {
                //a broken manifest shouldn't stop the neighbourhood from being listed or played.
                return null;
            }
        }

        /// <summary>
        /// All neighbourhoods the player can switch to, ordered by slot.
        /// </summary>
        public static List<NeighborhoodSlot> ListSlots()
        {
            var result = new Dictionary<int, NeighborhoodSlot>();
            foreach (var slot in SlotsIn(FSOEnvironment.UserDir))
            {
                var manifest = ReadManifest(SlotPath(slot));
                result[slot] = new NeighborhoodSlot()
                {
                    ID = slot,
                    Name = string.IsNullOrWhiteSpace(manifest?.Name) ? DefaultName(slot) : manifest.Name,
                    Author = manifest?.Author,
                    Description = manifest?.Description,
                    Imported = true,
                    Custom = manifest != null
                };
            }
            foreach (var slot in SlotsIn(TS1SavesPath))
            {
                if (result.ContainsKey(slot)) continue;
                result[slot] = new NeighborhoodSlot() { ID = slot, Name = DefaultName(slot) };
            }
            return result.Values.OrderBy(x => x.ID).ToList();
        }

        private static string DefaultName(int slot)
        {
            return "Neighborhood " + (slot + 1);
        }

        /// <summary>
        /// Neighbourhood packages in the user's Neighborhoods folder. Each is a folder with a neighborhood.json.
        /// </summary>
        public static List<NeighborhoodPackage> ListPackages()
        {
            var result = new List<NeighborhoodPackage>();
            var root = PackagesPath;
            if (!Directory.Exists(root)) return result;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var manifest = ReadManifest(dir);
                if (manifest == null) continue;
                result.Add(new NeighborhoodPackage() { Path = dir, Manifest = manifest });
            }
            return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ---- switching ----

        /// <summary>
        /// Loads the neighbourhood the player last used. Content always starts on slot 0, so this is called
        /// the first time the neighbourhood screen is shown.
        /// </summary>
        public static void ApplyStartupSlot()
        {
            if (StartupApplied) return;
            StartupApplied = true;
            try
            {
                Directory.CreateDirectory(PackagesPath);
            }
            catch (Exception) { }

            ActiveManifest = ReadManifest(SlotPath(0));

            int saved = 0;
            try
            {
                var file = Path.Combine(FSOEnvironment.UserDir, ActiveFile);
                if (File.Exists(file)) saved = JsonConvert.DeserializeObject<ActiveState>(File.ReadAllText(file))?.Active ?? 0;
            }
            catch (Exception) { }

            if (saved <= 0 || !IsNeighborhoodFolder(SlotPath(saved))) return;
            try
            {
                Switch(saved, false); //nothing has been played yet, so there is nothing to save
            }
            catch (Exception)
            {
                //Switch falls back to the previous neighbourhood by itself.
            }
        }

        private static void SaveActive()
        {
            try
            {
                File.WriteAllText(Path.Combine(FSOEnvironment.UserDir, ActiveFile),
                    JsonConvert.SerializeObject(new ActiveState() { Active = ActiveSlot }));
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Switches the game to another neighbourhood. Only call this from the neighbourhood screen, when no lot is loaded.
        /// Throws if the neighbourhood cannot be loaded, after returning to the previous one.
        /// </summary>
        public static void Switch(int slot, bool saveCurrent = true)
        {
            if (!IsNeighborhoodFolder(SlotPath(slot)) && !IsNeighborhoodFolder(Path.Combine(TS1SavesPath, FolderName(slot))))
                throw new FileNotFoundException("Neighborhood " + (slot + 1) + " does not exist.");

            var neigh = Content.Get().Neighborhood;
            var previous = ActiveSlot;

            //InitSpecific discards whatever is loaded, so write it out first.
            if (saveCurrent) neigh.SaveNeighbourhood(true);

            try
            {
                Load(neigh, slot);
            }
            catch (Exception)
            {
                if (slot != previous)
                {
                    try { Load(neigh, previous); } catch (Exception) { }
                }
                throw;
            }
            SaveActive();
        }

        private static void Load(TS1NeighborhoodProvider neigh, int slot)
        {
            //the provider expects this folder, but an otherwise fine neighbourhood without any sims may not have one.
            var path = SlotPath(slot);
            if (Directory.Exists(path) && FindEntry(path, "Characters", true) == null)
                Directory.CreateDirectory(Path.Combine(path, "Characters"));

            neigh.InitSpecific(slot);

            //InitSpecific unloads the last neighbourhood's sims, but leaves them in the person list.
            //AddMissingNeighbors would then try to add sims that no longer exist.
            var objs = Content.Get().WorldObjects as TS1ObjectProvider;
            if (objs?.Entries != null) objs.PersonGUIDs.RemoveWhere(x => !objs.Entries.ContainsKey(x));
            if (neigh.PreparePersonDataFromObject != null) neigh.AddMissingNeighbors();

            ActiveSlot = slot;
            ActiveManifest = ReadManifest(path);
            TextureCache.Clear();
        }

        // ---- creating ----

        private static int NextFreeSlot()
        {
            var used = new HashSet<int>(SlotsIn(FSOEnvironment.UserDir).Concat(SlotsIn(TS1SavesPath)));
            int slot = 1;
            while (used.Contains(slot) || Directory.Exists(SlotPath(slot))) slot++;
            return slot;
        }

        /// <summary>
        /// The empty neighbourhood TS1 ships with. Falls back to the first neighbourhood in the install.
        /// </summary>
        private static string FindTemplate()
        {
            var candidates = new string[]
            {
                FindEntry(Content.Get().TS1BasePath, "TemplateUserData", true),
                FindEntry(TS1SavesPath, "TemplateUserData", true),
                FindEntry(TS1SavesPath, "UserData", true),
                FindEntry(Content.Get().TS1BasePath, "UserData", true),
            };
            var template = candidates.FirstOrDefault(IsNeighborhoodFolder);
            if (template == null)
                throw new DirectoryNotFoundException("Could not find a neighborhood template (TemplateUserData) in The Sims install.");
            return template;
        }

        private static void CopyDirectory(string source, string destination, Func<string, bool> skip = null)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                if (skip != null && skip(file)) continue;
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            }
            foreach (var dir in Directory.GetDirectories(source))
            {
                if (skip != null && skip(dir)) continue;
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
            }
        }

        private static void WriteManifest(string folder, NeighborhoodManifest manifest)
        {
            var existing = FindEntry(folder, ManifestName, false);
            File.WriteAllText(existing ?? Path.Combine(folder, ManifestName), JsonConvert.SerializeObject(manifest, Formatting.Indented,
                new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore }));
        }

        /// <summary>
        /// Builds a new slot in a temporary folder, so that a failed copy never leaves a half made neighbourhood behind.
        /// </summary>
        private static int BuildSlot(Action<string> build)
        {
            var slot = NextFreeSlot();
            var final = SlotPath(slot);
            var temp = final + ".tmp";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            try
            {
                build(temp);
                if (!IsNeighborhoodFolder(temp)) throw new InvalidDataException("The neighborhood has no Neighborhood.iff.");
                Directory.CreateDirectory(Path.Combine(temp, "Characters"));
                Directory.Move(temp, final);
            }
            catch (Exception)
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (Exception) { }
                throw;
            }
            return slot;
        }

        /// <summary>
        /// Creates a new, empty neighbourhood from the TS1 template. Returns its slot.
        /// </summary>
        public static int CreateNew(string name)
        {
            var template = FindTemplate();
            return BuildSlot(temp =>
            {
                CopyDirectory(template, temp);
                WriteManifest(temp, new NeighborhoodManifest() { Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim() });
            });
        }

        /// <summary>
        /// Installs a neighbourhood package as a new save slot. Returns its slot.
        ///
        /// If the package has a UserData folder, that is the starting neighbourhood (lots, families, lot layout).
        /// Otherwise the TS1 template is used, and the package only provides a name and map screens.
        /// Everything else in the package (the manifest and its images) is copied alongside.
        /// </summary>
        public static int InstallPackage(NeighborhoodPackage package)
        {
            var data = FindEntry(package.Path, "UserData", true);
            if (data != null && !IsNeighborhoodFolder(data))
                throw new InvalidDataException("The UserData folder in \"" + package.Name + "\" has no Neighborhood.iff.");
            if (data == null) data = FindTemplate();

            return BuildSlot(temp =>
            {
                CopyDirectory(data, temp);
                CopyDirectory(package.Path, temp, x => string.Equals(Path.GetFileName(x), "UserData", StringComparison.OrdinalIgnoreCase));
            });
        }

        // ---- custom map screens ----

        /// <summary>
        /// The map screen for an area of the active neighbourhood, if its manifest customises it. Null otherwise.
        /// Anything the manifest leaves out is taken from the stock screen for that area.
        /// </summary>
        public static NeighborhoodViewConfig GetViewConfig(int mode, NeighborhoodViewConfig stock)
        {
            NeighborhoodAreaManifest area = null;
            if (ActiveManifest?.Areas == null || !ActiveManifest.Areas.TryGetValue(mode.ToString(), out area) || area == null) return null;

            var config = new NeighborhoodViewConfig()
            {
                Graphic = area.Graphic ?? stock.Graphic,
                Scale = (area.Scale != null && area.Scale > 0) ? area.Scale.Value : stock.Scale,
                Music = area.Music ?? stock.Music,
                BGSound = area.Ambience ?? stock.BGSound,
                Pulsate = area.Pulsate ?? stock.Pulsate,
                FrameDuration = (area.FrameDuration != null && area.FrameDuration > 0) ? area.FrameDuration.Value : stock.FrameDuration,
                FullImageAnimations = stock.FullImageAnimations
            };
            if (area.Animations != null)
            {
                config.FullImageAnimations = area.Animations
                    .Where(x => x?.Frames != null && x.Frames.Count > 0)
                    .Select(x => new NeighborhoodImageAnim(new Vector2(x.X, x.Y), x.Frames.ToArray()))
                    .ToArray();
            }
            else if (area.Graphic != null)
            {
                //the stock animations are drawn for the stock background.
                config.FullImageAnimations = new NeighborhoodImageAnim[0];
            }
            return config;
        }

        /// <summary>
        /// Gets a map screen image. Looks in the active neighbourhood's folder first, then in the TS1 content.
        /// </summary>
        public static Texture2D GetTexture(string name, GraphicsDevice gd)
        {
            if (name == null) return null;
            ITextureRef tex;
            if (!TextureCache.TryGetValue(name, out tex))
            {
                var file = ResolveFile(name);
                if (file != null) tex = new InMemoryTextureRef(File.ReadAllBytes(file));
                TextureCache[name] = tex;
            }
            if (tex == null) tex = Content.Get().TS1Global.Get(name) as ITextureRef;
            return tex?.Get(gd);
        }

        private static string ResolveFile(string name)
        {
            try
            {
                var root = Path.GetFullPath(SlotPath(ActiveSlot));
                var full = Path.GetFullPath(Path.Combine(root, name.Replace('\\', '/')));
                //manifests come from other people. don't let them read files from outside their neighbourhood.
                if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)) return null;
                return File.Exists(full) ? full : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
