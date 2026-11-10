/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Simitone.Client.Packs
{
    /// <summary>
    /// Optional add-on packs: folders in (user directory)/Packs/ that each hold a pack.json,
    /// plus the optional features that ship inside Simitone (see BuiltInPacks).
    /// Nothing in a pack is used unless the user enables it, and a change only applies on the next start,
    /// so the set of packs in use (Active) is fixed for the whole run.
    ///
    /// A pack's content/ folder is laid out like the game's own folder and is read alongside it, and the .piff
    /// files in its patch/ folder are applied to the game's objects. The code layer is not loaded yet.
    /// Anything else that packs add later should read Active (which is in dependency order) to decide what to load.
    /// </summary>
    public static class PackManager
    {
        /// <summary>Every built in pack and pack folder found, sorted by name. Includes broken ones so they can be shown.</summary>
        public static List<Pack> All { get; private set; } = new List<Pack>();

        /// <summary>The packs in use for this run, ordered so that each pack comes after the ones it requires.</summary>
        public static List<Pack> Active { get; private set; } = new List<Pack>();

        private static HashSet<string> ActiveIds = new HashSet<string>();
        private static HashSet<string> Enabled = new HashSet<string>();
        private static HashSet<string> EnabledAtStartup = new HashSet<string>();

        public static string PacksDir => Path.Combine(FSOEnvironment.UserDir, "Packs");
        private static string StatePath => Path.Combine(FSOEnvironment.UserDir, "packs.json");

        /// <summary>True when the enabled packs differ from the ones this run started with.</summary>
        public static bool RestartRequired => !Enabled.SetEquals(EnabledAtStartup);

        public static bool IsEnabled(Pack pack) => Enabled.Contains(pack.Id);

        /// <summary>Whether a pack is in use for this run. Cheap enough to ask every frame.</summary>
        public static bool IsActive(string id) => ActiveIds.Contains(id);

        /// <summary>
        /// Find the installed packs and work out which are in use. Call once, before content is loaded.
        /// </summary>
        public static void Init()
        {
            All = Discover(PacksDir);
            Enabled = LoadEnabled();
            EnabledAtStartup = new HashSet<string>(Enabled);
            Active = ResolveActive();
            ActiveIds = new HashSet<string>(Active.Select(x => x.Id));
            RegisterContent();

            foreach (var pack in All)
            {
                var problem = pack.LoadError ?? (IsEnabled(pack) ? GetProblem(pack) : null);
                var state = Active.Contains(pack) ? "active" : (IsEnabled(pack) ? "enabled but NOT loaded" : "disabled");
                Console.WriteLine("Pack " + pack.Id + " (" + pack.LayerSummary + "): " + state + (problem == null ? "" : " - " + problem));
            }
        }

        /// <summary>
        /// Tell the content system about the active packs' files. It reads these lists when it loads, after this.
        /// Later packs win: a loose file replaces a same named one from the game or an earlier pack.
        /// </summary>
        private static void RegisterContent()
        {
            FSO.Content.Content.TS1ExtraRoots.Clear();
            FSO.Content.Content.ExtraPatchPaths.Clear();
            foreach (var pack in Active)
            {
                if (pack.Folder == null) continue;
                if (pack.HasContent) FSO.Content.Content.TS1ExtraRoots.Add(Path.GetFullPath(Path.Combine(pack.Folder, "content")));
                if (pack.HasPatches) FSO.Content.Content.ExtraPatchPaths.Add(Path.GetFullPath(Path.Combine(pack.Folder, "patch")) + Path.DirectorySeparatorChar);
            }
        }

        private static List<Pack> Discover(string dir)
        {
            //built in packs come first, so a folder cannot take over one of their ids
            var result = BuiltInPacks.Create();
            var seen = result.ToDictionary(x => x.Id);
            try
            {
                Directory.CreateDirectory(dir);
                foreach (var folder in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (!File.Exists(Path.Combine(folder, Pack.ManifestName))) continue;
                    var pack = Pack.Load(folder);
                    if (pack.LoadError == null)
                    {
                        if (seen.TryGetValue(pack.Id, out var first))
                        {
                            //the first folder keeps the id. this one cannot be told apart from it, so it can't be enabled.
                            pack.LoadError = first.BuiltIn ? "Same id as the built in pack \"" + first.DisplayName + "\""
                                : "Same id as the pack in \"" + Path.GetFileName(first.Folder) + "\"";
                            pack.Id = pack.Id + " (" + Path.GetFileName(folder) + ")";
                        }
                        else seen[pack.Id] = pack;
                    }
                    result.Add(pack);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not read the Packs folder: " + e.Message);
            }
            return result.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static HashSet<string> LoadEnabled()
        {
            var state = new PackState();
            try
            {
                if (File.Exists(StatePath)) state = JsonSerializer.Deserialize<PackState>(File.ReadAllText(StatePath)) ?? new PackState();
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not read packs.json, starting with the default packs: " + e.Message);
            }

            var enabled = new HashSet<string>(state.Enabled ?? new List<string>());
            //a default-on pack stays on unless the user turned it off - including one added by a Simitone update
            foreach (var pack in All)
            {
                if (pack.DefaultEnabled && state.Disabled?.Contains(pack.Id) != true) enabled.Add(pack.Id);
            }
            return enabled;
        }

        private static void SaveEnabled()
        {
            try
            {
                var state = new PackState()
                {
                    Enabled = Enabled.OrderBy(x => x).ToList(),
                    Disabled = All.Where(x => x.DefaultEnabled && !Enabled.Contains(x.Id)).Select(x => x.Id).OrderBy(x => x).ToList()
                };
                File.WriteAllText(StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions() { WriteIndented = true }));
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not save packs.json: " + e.Message);
            }
        }

        private class PackState
        {
            public List<string> Enabled { get; set; }

            /// <summary>Default-on packs the user has turned off. Everything else is off unless it is in Enabled.</summary>
            public List<string> Disabled { get; set; }
        }

        private static Pack Find(string id)
        {
            return All.FirstOrDefault(x => x.Id == id && x.LoadError == null);
        }

        /// <summary>
        /// Why this pack cannot run alongside the packs that are currently enabled, or null if it can.
        /// </summary>
        public static string GetProblem(Pack pack)
        {
            return GetProblem(pack, Enabled);
        }

        private static string GetProblem(Pack pack, HashSet<string> enabled)
        {
            if (pack.LoadError != null) return pack.LoadError;

            var min = pack.Manifest.MinSimitoneVersion;
            if (!string.IsNullOrWhiteSpace(min))
            {
                var need = ParseVersion(min);
                if (need == null) return "Its minimum Simitone version \"" + min + "\" is not a version number";
                if (need > ParseVersion(SimitoneVersion.Current)) return "Needs Simitone " + min + " or newer (this is " + SimitoneVersion.Current + ")";
            }

            foreach (var id in pack.Manifest.Requires)
            {
                var other = Find(id);
                if (other == null) return "Needs the pack \"" + id + "\", which is not installed";
                if (!enabled.Contains(id)) return "Needs the pack \"" + other.DisplayName + "\" to be enabled first";
            }

            foreach (var other in All)
            {
                if (other == pack || other.LoadError != null || !enabled.Contains(other.Id)) continue;
                if (pack.Manifest.Conflicts.Contains(other.Id) || other.Manifest.Conflicts.Contains(pack.Id))
                    return "Cannot be used together with \"" + other.DisplayName + "\"";
            }
            return null;
        }

        /// <summary>
        /// Enable or disable a pack and remember the choice. Returns null on success, or the reason it was refused.
        /// The change applies the next time the game starts.
        /// </summary>
        public static string SetEnabled(Pack pack, bool enable)
        {
            if (enable == IsEnabled(pack)) return null;
            if (enable)
            {
                var problem = GetProblem(pack);
                if (problem != null) return problem;
                Enabled.Add(pack.Id);
            }
            else
            {
                var dependant = All.FirstOrDefault(x => x.LoadError == null && IsEnabled(x) && x.Manifest.Requires.Contains(pack.Id));
                if (dependant != null) return "\"" + dependant.DisplayName + "\" needs this pack. Disable that one first";
                Enabled.Remove(pack.Id);
            }
            SaveEnabled();
            return null;
        }

        /// <summary>
        /// The enabled packs that can really run, in dependency order. An enabled pack can still be unusable,
        /// eg. the pack it requires was deleted or the game was downgraded - leaving it out also leaves out its dependants.
        /// </summary>
        private static List<Pack> ResolveActive()
        {
            var usable = new HashSet<string>(Enabled.Where(id => Find(id) != null));
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var id in usable.ToList())
                {
                    if (GetProblem(Find(id), usable) != null)
                    {
                        usable.Remove(id);
                        changed = true;
                    }
                }
            }

            //each pass takes the packs whose requirements are already in the list. packs that require each other never get in.
            var result = new List<Pack>();
            var remaining = All.Where(x => x.LoadError == null && usable.Contains(x.Id)).ToList();
            while (remaining.Count > 0)
            {
                var ready = remaining.Where(x => x.Manifest.Requires.All(id => result.Any(y => y.Id == id))).ToList();
                if (ready.Count == 0) break;
                result.AddRange(ready);
                remaining.RemoveAll(x => ready.Contains(x));
            }
            return result;
        }

        /// <summary>
        /// The leading numbers of a version, so "0.8.22-forked" compares as 0.8.22. Null if there are none.
        /// </summary>
        private static Version ParseVersion(string text)
        {
            var match = Regex.Match(text.Trim(), @"^v?(\d+(\.\d+){0,3})");
            if (!match.Success) return null;
            var numbers = match.Groups[1].Value;
            if (!numbers.Contains('.')) numbers += ".0";
            return Version.TryParse(numbers, out var version) ? version : null;
        }
    }
}
