using System;

namespace Simitone.Client.Services.Discord
{
    /// <summary>Where the player is, as far as the presence cares.</summary>
    public enum PresencePlace
    {
        Starting,
        Neighbourhood,
        CreateFamily,
        Lot
    }

    /// <summary>What the game reports each check (see <see cref="PresenceSource"/>); plain data, so it can be tested.</summary>
    public class PresenceInfo
    {
        public PresencePlace Place;
        /// <summary>Neighbourhood map number (1-based), in the neighbourhood.</summary>
        public int Neighbourhood;
        /// <summary>Create a Family: 0 choosing a family, 1 the family, 2 a Sim.</summary>
        public int CasStage;
        /// <summary>On a lot: the family playing (raw name, e.g. "Goth"), or null.</summary>
        public string Family;
        /// <summary>On a lot: its name or address, or null.</summary>
        public string Lot;
        /// <summary>On a lot: "Live mode", "Buy mode", "Build mode" or "Options".</summary>
        public string Mode;
    }

    /// <summary>
    /// The two lines shown on a Discord profile, from a <see cref="PresenceInfo"/>, and the start of the "elapsed"
    /// timer: it restarts when the place changes (another lot, back to the neighbourhood), not when the mode does.
    /// </summary>
    public class PresenceText
    {
        public string LargeImage = "simitone";
        public string LargeText = "Simitone";

        private string PlaceKey;
        private long Start;

        public PresenceActivity Build(PresenceInfo info, DateTimeOffset now)
        {
            var key = info.Place + "|" + info.Neighbourhood + "|" + info.Lot;
            if (key != PlaceKey)
            {
                PlaceKey = key;
                Start = now.ToUnixTimeSeconds();
            }

            string details, state;
            switch (info.Place)
            {
                case PresencePlace.Neighbourhood:
                    details = "In the neighbourhood";
                    state = NeighbourhoodName(info.Neighbourhood);
                    break;
                case PresencePlace.CreateFamily:
                    details = "Create a Family";
                    state = (info.CasStage == 2) ? "Making a Sim" : (info.CasStage == 1) ? "Editing a family" : "Choosing a family";
                    break;
                case PresencePlace.Lot:
                    details = string.IsNullOrWhiteSpace(info.Family) ? (info.Lot ?? "On a lot") : "Playing " + FamilyTitle(info.Family);
                    state = (string.IsNullOrWhiteSpace(info.Family) || string.IsNullOrWhiteSpace(info.Lot))
                        ? info.Mode : $"{info.Mode} at {info.Lot}";
                    break;
                default:
                    details = "Starting up";
                    state = null;
                    break;
            }
            return new PresenceActivity
            {
                Details = details,
                State = state,
                Start = Start,
                LargeImage = LargeImage,
                LargeText = LargeText
            };
        }

        /// <summary>"Goth" to "the Goth family"; a name that already says "family" is left alone.</summary>
        public static string FamilyTitle(string name)
        {
            name = name.Trim();
            if (name.EndsWith("family", StringComparison.OrdinalIgnoreCase))
                return name.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? name : "the " + name;
            return "the " + name + " family";
        }

        /// <summary>The neighbourhood maps, numbered as the game's neighbourhood screen numbers them.</summary>
        public static string NeighbourhoodName(int mode)
        {
            switch (mode)
            {
                case 2: return "Downtown";
                case 3: return "Vacation Island";
                case 4: return "Old Town";
                case 5: return "Studio Town";
                case 7: return "Magic Town";
                default: return "Neighbourhood";
            }
        }
    }
}
