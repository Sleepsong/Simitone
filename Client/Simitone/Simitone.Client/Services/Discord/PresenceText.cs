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
        /// <summary>Neighbourhood map number (1-based): the map shown, or on a lot, the town it's in.</summary>
        public int Neighbourhood;
        /// <summary>In the neighbourhood place: it's a town map a Sim is travelling to from a lot, not the neighbourhood screen.</summary>
        public bool Travelling;
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
        /// <summary>
        /// The small picture in the corner of the large one, naming where the player is: buy, build, options; cas;
        /// downtown, vacation, studio, magic. Art assets of those names must be uploaded to the Discord application.
        /// </summary>
        public bool SmallImages = true;

        private string PlaceKey;
        private long Start;

        public PresenceActivity Build(PresenceInfo info, DateTimeOffset now)
        {
            var key = info.Place + "|" + info.Neighbourhood + "|" + info.Travelling + "|" + info.Lot;
            if (key != PlaceKey)
            {
                PlaceKey = key;
                Start = now.ToUnixTimeSeconds();
            }

            string details, state;
            switch (info.Place)
            {
                case PresencePlace.Neighbourhood:
                    details = info.Travelling ? "Travelling" : "In the neighbourhood";
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
            var (small, smallText) = SmallImages ? SmallImage(info) : (null, null);
            return new PresenceActivity
            {
                Details = details,
                State = state,
                Start = Start,
                LargeImage = LargeImage,
                LargeText = LargeText,
                SmallImage = small,
                SmallText = smallText
            };
        }

        /// <summary>The small picture's asset name and hover text for a place, or nulls (live mode, the main neighbourhood).</summary>
        public static (string image, string text) SmallImage(PresenceInfo info)
        {
            switch (info.Place)
            {
                case PresencePlace.CreateFamily:
                    return ("cas", "Create a Family");
                case PresencePlace.Neighbourhood:
                    return TownImage(info.Neighbourhood);
                case PresencePlace.Lot:
                    switch (info.Mode)
                    {
                        case "Buy mode": return ("buy", "Buy mode");
                        case "Build mode": return ("build", "Build mode");
                        case "Options": return ("options", "Options");
                    }
                    return TownImage(info.Neighbourhood); //live mode: the town the lot is in, if not the main one
            }
            return (null, null);
        }

        private static (string image, string text) TownImage(int neighbourhood)
        {
            switch (neighbourhood)
            {
                case 2: return ("downtown", "Downtown");
                case 3: return ("vacation", "Vacation Island");
                case 5: return ("studio", "Studio Town");
                case 7: return ("magic", "Magic Town");
            }
            return (null, null);
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
