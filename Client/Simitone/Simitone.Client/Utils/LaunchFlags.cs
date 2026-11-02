using FSO.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// Command line switches, e.g. "-logs" (in Steam: the game's Properties, Launch Options). A switch matches with
    /// one or two leading dashes or a slash, in any case.
    /// </summary>
    public static class LaunchFlags
    {
        /// <summary>
        /// -logs: write the diagnostic logs in the user folder: simantics-errors.log (script errors and notes, with
        /// the full stack), gamepad.log (controllers, focus) and, where there is one, discord.log. crash.log is always
        /// written, since a crash leaves nothing else.
        /// </summary>
        public static bool Logs => Has("logs");

        /// <summary>-travellog: write travel.log, the step-by-step trace of trips between lots (see TRAVEL_FIXES.md).</summary>
        public static bool TravelLog => Has("travellog");

        public static bool Has(string flag) => Parse(FSOEnvironment.Args).Contains(flag.ToLowerInvariant());

        /// <summary>The switches in a command line, lower case, without their dashes.</summary>
        public static HashSet<string> Parse(string args)
        {
            var result = new HashSet<string>();
            foreach (var part in (args ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!(part.StartsWith("-") || part.StartsWith("/"))) continue;
                var name = part.TrimStart('-', '/').ToLowerInvariant();
                if (name.Length > 0) result.Add(name);
            }
            return result;
        }
    }
}
