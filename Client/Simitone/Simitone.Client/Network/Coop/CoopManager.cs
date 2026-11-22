using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// The co-op session of this game, if it has one. It belongs to no screen: the game swaps whole screens when
    /// moving between the neighbourhood, a lot and Create-a-Family, and the session has to outlive them all.
    ///
    /// A host's screens tell it where the host is (<see cref="CoopScene"/>), and it tells the guests. A guest's
    /// screen is driven from here to follow: it shows the host's neighbourhood while the host is choosing a
    /// house, and the lot while the host is on one.
    /// </summary>
    public static class CoopManager
    {
        /// <summary>The session this game is hosting, or null.</summary>
        public static CoopHost Host { get; private set; }
        /// <summary>The session this game has joined as a guest, or null.</summary>
        public static CoopClient Guest { get; private set; }

        public static bool IsGuest { get { return Guest != null; } }
        /// <summary>True while we host and at least one guest is playing.</summary>
        public static bool HasGuests { get { return Host != null && Host.GuestCount > 0; } }

        /// <summary>A line telling a guest what it is waiting for, or null. The game screen shows it.</summary>
        public static string Status { get; private set; }

        private static TS1GameScreen Game { get { return GameFacade.Screens?.CurrentUIScreen as TS1GameScreen; } }

        /// <summary>
        /// Pumps the session. Called once a frame from the game loop, whichever screen is showing.
        /// </summary>
        public static void Update()
        {
            Host?.Update();
            Guest?.Update(); //can end the session
            if (Guest != null) CheckGuestWorld();
        }

        private static bool StartupDone;

        /// <summary>
        /// Called when the game first reaches the neighbourhood: starts hosting if that was asked for (see CoopOptions).
        /// </summary>
        public static void Startup()
        {
            if (StartupDone) return;
            StartupDone = true;
            if (CoopOptions.HostPort > 0 && !CoopOptions.JoinPending) StartHosting(CoopOptions.HostPort);
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024 * 1024) return Math.Max(1, bytes / 1024) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
        }

        #region Hosting

        //where the host is, as last told by its screens. Tracked even when not hosting.
        private static CoopScene Scene = CoopScene.None;
        private static ushort SceneArea = 4;
        private static int SceneHouse;
        private static int SceneMoveIn = -1;

        /// <summary>
        /// Opens this game to guests. They can join wherever the host is, and follow it from then on.
        /// </summary>
        public static bool StartHosting(int port)
        {
            if (Host != null || Guest != null) return false;
            var session = new CoopHost();
            try
            {
                session.Start(port);
            }
            catch (Exception e)
            {
                Console.WriteLine("COOP could not host on port " + port + ": " + e.Message);
                return false;
            }
            //what guests compare their own objects with, and fetch what they lack from
            session.Content = CoopContentScanner.BuildLocalAsync();
            session.ReadHouse = CoopWorld.ReadHouse;
            session.Greeting = Greet;
            session.OnContentRequest += (name, files, bytes) =>
            {
                Console.WriteLine("COOP sending " + files + " object files (" + FormatSize(bytes) + ") to " + (name == "" ? "a guest" : name));
            };
            session.OnSpeedRequest += (speed) =>
            {
                if (CoopOptions.GuestsControlSpeed) Game?.ChangeSpeedTo(speed);
            };
            session.OnGuestChange += (name, joined) =>
            {
                Console.WriteLine("COOP " + (name == "" ? "a guest" : name) + (joined ? " joined" : " left") + " (" + session.GuestCount + " connected)");
            };
            Host = session;
            Console.WriteLine("COOP hosting on port " + port);
            return true;
        }

        public static void StopHosting()
        {
            if (Host == null) return;
            Host.Shutdown();
            Host = null;
            Console.WriteLine("COOP stopped hosting");
        }

        /// <summary>
        /// What a guest is told the moment it is ready: where the host is, and its neighbourhood if it is looking at it.
        /// </summary>
        private static List<KeyValuePair<CoopFrameKind, byte[]>> Greet()
        {
            var frames = new List<KeyValuePair<CoopFrameKind, byte[]>>();
            if (Scene == CoopScene.Neighbourhood)
            {
                var lobby = CaptureLobby();
                if (lobby != null) frames.Add(new KeyValuePair<CoopFrameKind, byte[]>(CoopFrameKind.Lobby, lobby));
            }
            frames.Add(new KeyValuePair<CoopFrameKind, byte[]>(CoopFrameKind.Scene, PackScene()));
            return frames;
        }

        private static byte[] CaptureLobby()
        {
            try
            {
                return CoopWorld.Capture().Pack();
            }
            catch (Exception e)
            {
                Console.WriteLine("COOP could not capture the neighbourhood for guests: " + e.Message);
                return null;
            }
        }

        private static byte[] PackScene()
        {
            var data = new byte[11];
            data[0] = (byte)Scene;
            BitConverter.TryWriteBytes(new Span<byte>(data, 1, 2), SceneArea);
            BitConverter.TryWriteBytes(new Span<byte>(data, 3, 4), SceneHouse);
            BitConverter.TryWriteBytes(new Span<byte>(data, 7, 4), SceneMoveIn);
            return data;
        }

        private static void SendScene()
        {
            if (HasGuests) Host.SendToGuests(CoopFrameKind.Scene, PackScene());
        }

        private static void SendLobby()
        {
            if (!HasGuests) return;
            var lobby = CaptureLobby();
            if (lobby != null) Host.SendToGuests(CoopFrameKind.Lobby, lobby);
        }

        /// <summary>
        /// The host's screen now shows the neighbourhood, as it is in memory right now.
        /// </summary>
        public static void ShowNeighbourhood(ushort area, int? moveInFamily)
        {
            Scene = CoopScene.Neighbourhood;
            SceneArea = area;
            SceneHouse = 0;
            SceneMoveIn = moveInFamily ?? -1;
            SendLobby();
            SendScene();
        }

        /// <summary>The host switched between the neighbourhood, downtown, vacation island and so on.</summary>
        public static void ShowArea(ushort area)
        {
            if (Scene != CoopScene.Neighbourhood) return;
            SceneArea = area;
            SceneHouse = 0;
            SendScene();
        }

        /// <summary>The host opened a house's details (its number), or closed them (0).</summary>
        public static void ViewHouse(int house)
        {
            if (Scene != CoopScene.Neighbourhood || SceneHouse == house) return;
            SceneHouse = house;
            SendScene();
        }

        /// <summary>The host is choosing a house for this family (null: just browsing).</summary>
        public static void SetMoveIn(int? family)
        {
            SceneMoveIn = family ?? -1;
            if (Scene == CoopScene.Neighbourhood) SendScene();
        }

        /// <summary>The host changed the neighbourhood while looking at it (evicted a family, say).</summary>
        public static void NeighbourhoodChanged()
        {
            if (Scene != CoopScene.Neighbourhood) return;
            SendLobby();
            SendScene();
        }

        /// <summary>
        /// The host is loading a lot, which this driver will run. Guests are told, then given the lot.
        /// </summary>
        public static void EnterLot(CoopServerDriver driver)
        {
            Scene = CoopScene.Lot;
            SceneHouse = 0;
            if (Host == null) return;
            SendScene();
            Host.Attach(driver);
        }

        /// <summary>The host's lot is closing. Where it goes next is told when it gets there.</summary>
        public static void LeaveLot()
        {
            Host?.Attach(null);
        }

        public static void EnterCreateFamily()
        {
            Scene = CoopScene.CreateFamily;
            SceneHouse = 0;
            SendScene();
        }

        #endregion

        #region Guest

        private static CoopScene HostScene;
        private static ushort HostArea = 4;
        private static int HostHouse;
        private static int HostMoveIn = -1;
        private static CoopWorld WorldSeen;       //the host's neighbourhood we last asked for houses for
        private static bool NeighbourhoodStale;   //the neighbourhood view has to be rebuilt from new data
        private static HashSet<string> HousesAwaited = new HashSet<string>();

        /// <summary>
        /// Joins another player's game as a guest, from the neighbourhood screen. From here on that screen
        /// shows whatever the host is doing.
        /// </summary>
        public static void Join(string address, TS1GameScreen screen)
        {
            if (Guest != null || Host != null) return;
            var guest = new CoopClient();
            //the host's objects are compared with ours, and the ones we lack are fetched into the cache
            guest.LocalContent = CoopContentScanner.BuildLocalAsync();
            guest.CacheDir = CoopContentScanner.ObjectCacheDir;
            guest.ApplyContent = CoopContentScanner.Apply;
            var lastPercent = -1;
            guest.OnContentProgress += (done, total, filesLeft) =>
            {
                var percent = (total == 0) ? 100 : (int)(done * 100 / total);
                if (done == 0) Console.WriteLine("COOP fetching " + filesLeft + " object files from the host (" + FormatSize(total) + ")");
                else if (percent / 10 != lastPercent / 10) Console.WriteLine("COOP fetched " + percent + "%");
                lastPercent = percent;
                Status = "Fetching the host's objects: " + FormatSize(done) + " of " + FormatSize(total) + " (" + filesLeft + " files to go)";
            };
            guest.OnWelcome += () =>
            {
                Console.WriteLine("COOP joined as player " + guest.PlayerID +
                    ((guest.ContentFilesFetched > 0) ? " after fetching " + guest.ContentFilesFetched + " object files" : ""));
                Status = "Waiting for the host...";
            };
            guest.OnDirect += (data) =>
            {
                var game = Game;
                if (game?.vm != null) guest.Driver.HandleDirect(game.vm, data);
            };
            guest.OnLobby += GuestLobby;
            guest.OnScene += (scene, area, house, moveIn) =>
            {
                if (scene != HostScene) Console.WriteLine("COOP the host is at: " + scene);
                HostScene = scene;
                HostArea = area;
                HostHouse = house;
                HostMoveIn = moveIn;
                ApplyGuestScene();
            };
            guest.OnHouseFile += GuestHouseFile;
            guest.OnClosed += (reason) =>
            {
                Console.WriteLine("COOP session ended: " + reason);
                EndGuest();
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = "Co-op",
                    Message = reason,
                    Buttons = UIAlertButton.Ok((b) => alert.Close())
                });
                UIScreen.GlobalShowDialog(alert, true);
            };

            HostScene = CoopScene.None;
            HostArea = 4;
            HostHouse = 0;
            HostMoveIn = -1;
            WorldSeen = null;
            NeighbourhoodStale = false;
            HousesAwaited.Clear();
            Guest = guest;
            Status = "Joining " + address + "...";
            screen.GuestHideNeighbourhood(); //our own neighbourhood: the host's replaces it
            Console.WriteLine("COOP joining " + address);
            guest.Connect(address, GlobalSettings.Default.LastUser ?? "");
        }

        /// <summary>
        /// Leaves the session this game joined, and goes back to its own neighbourhood.
        /// </summary>
        public static void Leave()
        {
            if (Guest == null) return;
            Guest.Disconnect();
            Console.WriteLine("COOP left the session");
            EndGuest();
        }

        private static void EndGuest()
        {
            Guest = null;
            Status = null;
            HousesAwaited.Clear();
            WorldSeen = null;
            var game = Game;
            if (game != null) game.CoopEnded();
            else
            {
                //no game screen to tidy up, but our own neighbourhood and objects still have to come back
                CoopWorld.Restore();
                CoopContentScanner.Restore();
            }
        }

        /// <summary>The lot has appeared on the guest's screen, so there is nothing left to wait for.</summary>
        public static void GuestLotShown()
        {
            if (Guest != null && HostScene == CoopScene.Lot) Status = null;
        }

        private static void GuestLobby(byte[] data)
        {
            try
            {
                CoopWorld.Unpack(data).Apply();
            }
            catch (Exception e)
            {
                Guest?.Fail("The host's neighbourhood could not be loaded.\n\n" + e.Message);
                return;
            }
            CheckGuestWorld();
            ApplyGuestScene();
        }

        /// <summary>
        /// When a new copy of the host's neighbourhood has been loaded (for the neighbourhood screen, or along
        /// with a lot), asks the host for the house files that go with it and that we do not have yet.
        /// </summary>
        private static void CheckGuestWorld()
        {
            var world = CoopWorld.AppliedWorld;
            if (world == null || world == WorldSeen) return;
            WorldSeen = world;
            NeighbourhoodStale = true;
            List<string> missing;
            try
            {
                missing = world.MissingHouses();
            }
            catch (Exception)
            {
                return;
            }
            HousesAwaited = new HashSet<string>(missing);
            if (missing.Count > 0) Guest.RequestHouses(missing);
        }

        private static void GuestHouseFile(string name, byte[] data, int start, int count)
        {
            if (count > 0)
            {
                try
                {
                    CoopWorld.SaveHouse(name, data, start, count);
                }
                catch (Exception e)
                {
                    Console.WriteLine("COOP could not save " + name + ": " + e.Message);
                }
            }
            if (!HousesAwaited.Remove(name)) return;
            if (HousesAwaited.Count == 0)
            {
                NeighbourhoodStale = true;
                ApplyGuestScene();
            }
            else if (HostScene == CoopScene.Neighbourhood)
            {
                Status = "Loading the host's neighbourhood (" + HousesAwaited.Count + " houses to go)...";
            }
        }

        /// <summary>
        /// Makes the guest's screen show what the host's does.
        /// </summary>
        private static void ApplyGuestScene()
        {
            var game = Game;
            if (game == null || Guest == null) return;
            switch (HostScene)
            {
                case CoopScene.Lot:
                    if (!game.InLot)
                    {
                        Status = "Loading the host's lot... (Esc to leave)";
                        game.GuestEnterLot();
                    }
                    break;
                case CoopScene.Neighbourhood:
                    if (game.InLot) game.GuestLeaveLot();
                    if (!CoopWorld.Applied || HousesAwaited.Count > 0)
                    {
                        //the pictures of the houses are still on their way
                        game.GuestHideNeighbourhood();
                        Status = "Loading the host's neighbourhood" + ((HousesAwaited.Count > 0) ? " (" + HousesAwaited.Count + " houses to go)" : "") + "...";
                        break;
                    }
                    Status = "The host is choosing a house (Esc to leave)";
                    game.GuestShowNeighbourhood(HostArea, HostHouse, HostMoveIn, NeighbourhoodStale);
                    NeighbourhoodStale = false;
                    break;
                case CoopScene.CreateFamily:
                    if (game.InLot) game.GuestLeaveLot();
                    game.GuestHideNeighbourhood();
                    Status = "The host is creating a family... (Esc to leave)";
                    break;
                default:
                    if (game.InLot) game.GuestLeaveLot();
                    game.GuestHideNeighbourhood();
                    Status = "Waiting for the host... (Esc to leave)";
                    break;
            }
        }

        #endregion
    }
}
