/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Common;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Simitone.Client.Packs
{
    /// <summary>
    /// A pack's own data for a lot, kept in a file beside the saves rather than inside them, so the save format is the
    /// same with or without the pack. A pack writes it from ISimitonePack.OnLotSaved and reads it back in OnLotLoaded.
    /// What is in the data is entirely the pack's business.
    /// </summary>
    public static class LotPackData
    {
        private static string PathFor(string packId, string lotPath)
        {
            //the lot's file name, to be readable, plus a hash of its whole path: different neighbourhoods reuse the names
            var full = Path.GetFullPath(lotPath).Replace('\\', '/');
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))).Substring(0, 12).ToLowerInvariant();
            var name = new string(Path.GetFileNameWithoutExtension(full).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            var pack = new string(packId.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray());
            return Path.Combine(FSOEnvironment.UserDir, "LocalHouse", "PackData", pack, name + "-" + hash + ".dat");
        }

        /// <summary>The data this pack last wrote for this lot, or null if there is none.</summary>
        public static byte[] Read(string packId, string lotPath)
        {
            try
            {
                var path = PathFor(packId, lotPath);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not read pack data for " + packId + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Keep this pack's data for this lot. Null means the pack has nothing to keep, and removes what was there.</summary>
        public static void Write(string packId, string lotPath, byte[] data)
        {
            try
            {
                var path = PathFor(packId, lotPath);
                if (data == null)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, data);
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not save pack data for " + packId + ": " + e.Message);
            }
        }
    }
}
