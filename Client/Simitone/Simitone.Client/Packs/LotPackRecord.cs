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

namespace Simitone.Client.Packs
{
    /// <summary>
    /// Remembers which packs each lot was last saved with, so that loading it without one of them can warn the user
    /// before they save over it. Kept in its own file beside the saves rather than inside them, so the save format
    /// is the same with or without packs.
    /// </summary>
    public static class LotPackRecord
    {
        private static string RecordPath => Path.Combine(FSOEnvironment.UserDir, "LocalHouse", "lotpacks.json");

        /// <summary>Lot file path -> pack id -> the pack's name when it was saved (the pack may be gone when we need it).</summary>
        private static Dictionary<string, Dictionary<string, string>> Read()
        {
            try
            {
                if (File.Exists(RecordPath))
                    return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(RecordPath))
                        ?? new Dictionary<string, Dictionary<string, string>>();
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not read lotpacks.json: " + e.Message);
            }
            return new Dictionary<string, Dictionary<string, string>>();
        }

        private static string Key(string lotPath)
        {
            return Path.GetFullPath(lotPath).Replace('\\', '/');
        }

        /// <summary>
        /// The packs that can leave something in a saved lot. Built in packs are left out: they keep their own data
        /// and a lot loads the same without them.
        /// </summary>
        private static IEnumerable<Pack> SaveAffecting()
        {
            return PackManager.Active.Where(x => !x.BuiltIn && (x.HasContent || x.HasPatches || x.HasCode));
        }

        /// <summary>
        /// Note that this lot has just been saved with the packs active now.
        /// </summary>
        public static void Saved(string lotPath)
        {
            try
            {
                var record = Read();
                var packs = SaveAffecting().ToDictionary(x => x.Id, x => x.DisplayName);
                if (packs.Count > 0) record[Key(lotPath)] = packs;
                else if (!record.Remove(Key(lotPath))) return; //nothing to record and nothing recorded: leave no file behind
                Directory.CreateDirectory(Path.GetDirectoryName(RecordPath));
                File.WriteAllText(RecordPath, JsonSerializer.Serialize(record, new JsonSerializerOptions() { WriteIndented = true }));
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not save lotpacks.json: " + e.Message);
            }
        }

        /// <summary>
        /// Names of the packs this lot was last saved with that are not active now. Empty when nothing is missing.
        /// </summary>
        public static List<string> Missing(string lotPath)
        {
            if (!Read().TryGetValue(Key(lotPath), out var packs)) return new List<string>();
            return packs.Where(x => !PackManager.IsActive(x.Key)).Select(x => x.Value ?? x.Key).OrderBy(x => x).ToList();
        }
    }
}
