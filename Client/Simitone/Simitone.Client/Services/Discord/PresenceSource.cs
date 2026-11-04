using FSO.Client;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;

namespace Simitone.Client.Services.Discord
{
    /// <summary>Reads where the player is from the current screen, for the presence. Runs on the game thread.</summary>
    public static class PresenceSource
    {
        public static PresenceInfo Read()
        {
            var screen = GameFacade.Screens?.CurrentUIScreen;
            if (screen is TS1CASScreen cas)
                return new PresenceInfo { Place = PresencePlace.CreateFamily, CasStage = (int)cas.CurrentMode };

            if (screen is TS1GameScreen game)
            {
                var map = game.ActiveNeighPanel; //the neighbourhood, or a town map a Sim is travelling to
                if (map != null)
                    return new PresenceInfo { Place = PresencePlace.Neighbourhood, Neighbourhood = map.NeighbourhoodMode, Travelling = !game.InNeighbourhood };
                if (game.vm != null && game.vm.Ready && game.Frontend != null)
                {
                    return new PresenceInfo
                    {
                        Place = PresencePlace.Lot,
                        Family = FamilyName(game.ActiveFamily),
                        Lot = LotName(game.CurrentHouse),
                        Mode = ModeName(game.Frontend.MainPanel.Mode),
                        Neighbourhood = (game.CurrentHouse > 0) ? TS1GameScreen.GetNeighborhoodModeFromHouse(game.CurrentHouse) : 0
                    };
                }
            }
            return new PresenceInfo { Place = PresencePlace.Starting };
        }

        private static string ModeName(UIMainPanelMode mode)
        {
            switch (mode)
            {
                case UIMainPanelMode.BUY: return "Buy mode";
                case UIMainPanelMode.BUILD: return "Build mode";
                case UIMainPanelMode.OPTIONS: return "Options";
                default: return "Live mode";
            }
        }

        private static string FamilyName(FSO.Files.Formats.IFF.Chunks.FAMI family)
        {
            if (family == null) return null;
            return Content.Get().Neighborhood.MainResource.Get<FAMs>(family.ChunkID)?.GetString(0);
        }

        /// <summary>The lot's name, else its street address (as the neighbourhood's house tooltips have it).</summary>
        private static string LotName(int house)
        {
            if (house <= 0) return null;
            var neigh = Content.Get().Neighborhood;
            var name = neigh.GetHouseNameDesc(house).Item1?.Trim().TrimEnd(':').TrimEnd(); //some end in a colon
            if (!string.IsNullOrEmpty(name)) return name;
            var assignment = neigh.StreetNames.Get<STR>(2001)?.GetString(house - 1);
            if (int.TryParse(assignment, out var street))
                return neigh.StreetNames.Get<STR>(2000)?.GetString(street - 1)?.Replace("%s", house.ToString());
            return null;
        }
    }
}
