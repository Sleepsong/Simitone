/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Simitone.Client.Packs
{
    /// <summary>
    /// The pack.json at the root of a pack folder. Only "id" is required.
    /// </summary>
    public class PackManifest
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("author")] public string Author { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }

        /// <summary>Lowest Simitone version this pack runs on, eg. "0.8.22".</summary>
        [JsonPropertyName("minSimitoneVersion")] public string MinSimitoneVersion { get; set; }

        /// <summary>Ids of packs that must be enabled for this one to work.</summary>
        [JsonPropertyName("requires")] public List<string> Requires { get; set; } = new List<string>();

        /// <summary>Ids of packs that cannot be enabled at the same time as this one.</summary>
        [JsonPropertyName("conflicts")] public List<string> Conflicts { get; set; } = new List<string>();

        /// <summary>Optional code layer: a .dll inside the pack folder. Reported, but not loaded yet.</summary>
        [JsonPropertyName("assembly")] public string Assembly { get; set; }
    }

    /// <summary>
    /// A pack folder found on disk, valid or not.
    /// </summary>
    public class Pack
    {
        public const string ManifestName = "pack.json";
        private static readonly Regex IdFormat = new Regex("^[a-z0-9][a-z0-9._-]{0,63}$");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>Unique id. Falls back to the folder name when the manifest cannot be read.</summary>
        public string Id;
        public string Folder;
        public PackManifest Manifest = new PackManifest();

        /// <summary>Why this pack can never be enabled as it is on disk (bad manifest, duplicate id), or null.</summary>
        public string LoadError;

        public bool HasContent;
        public bool HasPatches;
        public bool HasCode;

        public string DisplayName => string.IsNullOrWhiteSpace(Manifest.Name) ? Id : Manifest.Name;

        public static Pack Load(string folder)
        {
            var pack = new Pack() { Folder = folder, Id = Path.GetFileName(folder.TrimEnd('/', '\\')) };
            try
            {
                var manifest = JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(Path.Combine(folder, ManifestName)), JsonOptions);
                if (manifest == null) throw new Exception("it is empty");
                if (manifest.Id == null || !IdFormat.IsMatch(manifest.Id))
                    throw new Exception("\"id\" must be lowercase letters, digits, '.', '_' or '-'");
                manifest.Requires = manifest.Requires ?? new List<string>();
                manifest.Conflicts = manifest.Conflicts ?? new List<string>();
                if (manifest.Assembly != null)
                {
                    //the code layer must stay inside its own pack folder
                    var root = Path.GetFullPath(folder + Path.DirectorySeparatorChar);
                    if (!Path.GetFullPath(Path.Combine(folder, manifest.Assembly)).StartsWith(root))
                        throw new Exception("\"assembly\" must be a file inside the pack folder");
                }
                pack.Manifest = manifest;
                pack.Id = manifest.Id;
            }
            catch (Exception e)
            {
                pack.LoadError = "pack.json could not be read: " + e.Message;
            }

            pack.HasContent = Directory.Exists(Path.Combine(folder, "content"));
            pack.HasPatches = Directory.Exists(Path.Combine(folder, "patch"));
            pack.HasCode = pack.Manifest.Assembly != null;
            return pack;
        }

        /// <summary>
        /// eg. "Content, Code". What the pack would change, for the user to judge before enabling.
        /// </summary>
        public string LayerSummary
        {
            get
            {
                var layers = new List<string>();
                if (HasContent) layers.Add("Content");
                if (HasPatches) layers.Add("Patches");
                if (HasCode) layers.Add("Code");
                return layers.Count == 0 ? "Empty" : string.Join(", ", layers);
            }
        }
    }
}
