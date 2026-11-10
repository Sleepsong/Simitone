/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;

namespace Simitone.Client.Packs
{
    /// <summary>
    /// Features that ship inside Simitone but are switched on and off like packs, from the same list.
    /// To make a feature optional: add it here, then have its code check PackManager.IsActive(id).
    /// Like every pack, a change applies on the next start, so that check gives the same answer for a whole run.
    /// </summary>
    public static class BuiltInPacks
    {
        public const string Weather = "simitone.weather";

        public static List<Pack> Create()
        {
            return new List<Pack>()
            {
                //on by default: weather was always available before it became optional
                BuiltIn(Weather, "Weather", true, "Cosmetic rain, snow, hail and storms, set from the cheat console with the \"weather\" command."),
            };
        }

        private static Pack BuiltIn(string id, string name, bool defaultEnabled, string description)
        {
            return new Pack()
            {
                Id = id,
                BuiltIn = true,
                DefaultEnabled = defaultEnabled,
                Manifest = new PackManifest() { Id = id, Name = name, Description = description }
            };
        }
    }
}
