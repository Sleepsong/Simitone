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

        /// <summary>True while this game is in a session, hosting or as a guest.</summary>
        public static bool InSession { get { return Host != null || Guest != null; } }

        /// <summary>This game's player ID in the session.</summary>
        public static uint MyID { get { return (Guest != null) ? Guest.PlayerID : CoopProtocol.HostPlayerID; } }

        private static TS1GameScreen Game { get { return GameFacade.Screens?.CurrentUIScreen as TS1GameScreen; } }
        private static object ScreenSeen; //the screen the guest's view was last set up on

        /// <summary>
        /// Pumps the session. Called once a frame from the game loop, whichever screen is showing.
        /// </summary>
        public static void Update()
        {
            Host?.Update();
            Guest?.Update(); //can end the session
            if (Guest != null)
            {
                CheckGuestWorld();
                //the game swaps whole screens between the neighbourhood and Create-a-Family. A new one starts
                //out knowing nothing of the session, so set it up as soon as it appears.
                var screen = GameFacade.Screens?.CurrentUIScreen;
                if (screen != ScreenSeen && (screen is TS1GameScreen || screen is TS1CASScreen))
                {
                    NeighbourhoodStale = true;
                    ApplyGuestScene();
                }
            }
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
                if (!joined && CasHolder != CoopProtocol.HostPlayerID && session.GuestName(CasHolder) == null)
                {
                    SetCasHolder(CoopProtocol.HostPlayerID); //they left holding control of Create-a-Family
                }
                if (!joined) DropPointers(session);
            };
            session.OnPointer += HostPointer;
            session.OnCasEdit += HostCasEdit;
            session.OnCasRelease += (guestID) =>
            {
                if (guestID == CasHolder) SetCasHolder(CoopProtocol.HostPlayerID);
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
            Pointers.Clear();
            Console.WriteLine("COOP stopped hosting");
        }

        /// <summary>
        /// What a guest is told the moment it is ready: where the host is, and its neighbourhood if it is looking at it.
        /// </summary>
        private static List<KeyValuePair<CoopFrameKind, byte[]>> Greet()
        {
            var frames = new List<KeyValuePair<CoopFrameKind, byte[]>>();
            if (Scene == CoopScene.Neighbourhood || Scene == CoopScene.CreateFamily)
            {
                var lobby = CaptureLobby();
                if (lobby != null) frames.Add(new KeyValuePair<CoopFrameKind, byte[]>(CoopFrameKind.Lobby, lobby));
            }
            frames.Add(new KeyValuePair<CoopFrameKind, byte[]>(CoopFrameKind.Scene, PackScene()));
            if (Scene == CoopScene.CreateFamily) frames.Add(new KeyValuePair<CoopFrameKind, byte[]>(CoopFrameKind.Cas, PackCas()));
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
            ReportPointer(0, 0, false);
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
            if (Guest != null) return; //a guest's own Create-a-Family screen, following the host's
            Scene = CoopScene.CreateFamily;
            SceneHouse = 0;
            ReportPointer(0, 0, false);
            //a new family is started, with the host in control
            CasHolder = CoopProtocol.HostPlayerID;
            CasFamily = new CoopCasState().Pack();
            CasVersion++;
            SendScene();
            if (HasGuests) Host.SendToGuests(CoopFrameKind.Cas, PackCas());
        }

        #endregion

        #region Pointers on the neighbourhood

        /// <summary>
        /// Another player's mouse pointer on the neighbourhood screen. Its position is in the coordinates of the
        /// neighbourhood picture itself, so it is over the same house on every screen whatever its size or zoom.
        /// </summary>
        public class Pointer
        {
            public uint Player;
            public string Name;
            public float X;
            public float Y;
        }

        /// <summary>The other players' pointers that are on the neighbourhood right now, by player ID.</summary>
        public static readonly Dictionary<uint, Pointer> Pointers = new Dictionary<uint, Pointer>();

        private static float PointerX, PointerY;
        private static bool PointerShown;
        private static int PointerWait;

        /// <summary>
        /// Called every frame by the neighbourhood screen with where this player's mouse is on the picture, or
        /// that it is not on it (another screen is showing). The others are told when it moves, about thirty
        /// times a second at most.
        /// </summary>
        public static void ReportPointer(float x, float y, bool shown)
        {
            if (!InSession || (Guest != null && Guest.PlayerID == 0))
            {
                PointerShown = false;
                return;
            }
            if (shown == PointerShown && (!shown || (Math.Abs(x - PointerX) < 0.5f && Math.Abs(y - PointerY) < 0.5f))) return;
            if (shown && PointerShown && ++PointerWait < 2) return;
            PointerWait = 0;
            PointerX = x;
            PointerY = y;
            PointerShown = shown;
            if (Host != null)
            {
                if (HasGuests) Host.SendToGuests(CoopFrameKind.Pointers, PackPointer(CoopProtocol.HostPlayerID, x, y, shown));
            }
            else Guest.SendPointer(x, y, shown);
        }

        private static byte[] PackPointer(uint player, float x, float y, bool shown)
        {
            var data = new byte[13];
            BitConverter.TryWriteBytes(new Span<byte>(data, 0, 4), player);
            BitConverter.TryWriteBytes(new Span<byte>(data, 4, 4), x);
            BitConverter.TryWriteBytes(new Span<byte>(data, 8, 4), y);
            data[12] = (byte)(shown ? 1 : 0);
            return data;
        }

        private static void SetPointer(uint player, string name, float x, float y, bool shown)
        {
            //the picture is 800 by 600; anything far outside it is not a place on it
            if (!shown || !float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) > 8000 || Math.Abs(y) > 8000)
            {
                Pointers.Remove(player);
                return;
            }
            if (!Pointers.TryGetValue(player, out var pointer))
            {
                pointer = new Pointer() { Player = player };
                Pointers[player] = pointer;
            }
            pointer.Name = name;
            pointer.X = x;
            pointer.Y = y;
        }

        /// <summary>A guest's pointer moved: show it here, and pass it on to the other guests.</summary>
        private static void HostPointer(uint guestID, float x, float y, bool shown)
        {
            var name = Host?.GuestName(guestID);
            SetPointer(guestID, string.IsNullOrEmpty(name) ? "Guest" : name, x, y, shown);
            Host?.SendToGuests(CoopFrameKind.Pointers, PackPointer(guestID, x, y, shown && Pointers.ContainsKey(guestID)), guestID);
        }

        /// <summary>Forgets the pointers of guests that have left, here and on the other guests' screens.</summary>
        private static void DropPointers(CoopHost session)
        {
            foreach (var player in new List<uint>(Pointers.Keys))
            {
                if (session.GuestName(player) != null) continue;
                Pointers.Remove(player);
                session.SendToGuests(CoopFrameKind.Pointers, PackPointer(player, 0, 0, false));
            }
        }

        #endregion

        #region Create-a-Family, shared

        /// <summary>The player who may edit the family being created. Everyone else watches.</summary>
        public static uint CasHolder { get; private set; } = CoopProtocol.HostPlayerID;

        /// <summary>The family as it stands (a packed CoopCasState).</summary>
        public static byte[] CasFamily { get; private set; } = new CoopCasState().Pack();

        /// <summary>Goes up whenever the family or its holder was changed by someone other than this game's own screen.</summary>
        public static int CasVersion { get; private set; }

        /// <summary>True if this player is the one who may edit the family. Always true outside a session.</summary>
        public static bool CasMine { get { return !InSession || CasHolder == MyID; } }

        /// <summary>What to call the player holding control, for the status line.</summary>
        public static string CasHolderName
        {
            get
            {
                if (CasHolder == CoopProtocol.HostPlayerID) return "The host";
                var name = Host?.GuestName(CasHolder);
                return string.IsNullOrEmpty(name) ? "Another player" : name;
            }
        }

        private static byte[] PackCas()
        {
            var data = new byte[4 + CasFamily.Length];
            BitConverter.TryWriteBytes(new Span<byte>(data, 0, 4), CasHolder);
            Buffer.BlockCopy(CasFamily, 0, data, 4, CasFamily.Length);
            return data;
        }

        /// <summary>
        /// This player, holding control, changed the family on its Create-a-Family screen.
        /// </summary>
        public static void CasEdited(byte[] family)
        {
            if (!CasMine) return;
            CasFamily = family;
            if (Host != null)
            {
                if (HasGuests) Host.SendToGuests(CoopFrameKind.Cas, PackCas());
            }
            else Guest?.SendCasEdit(family);
        }

        /// <summary>
        /// The host hands control of the family to a guest: the next one after whoever has it now.
        /// With one guest, that is simply the guest.
        /// </summary>
        public static void CasGive()
        {
            if (Host == null) return;
            var guests = Host.GuestIDs();
            if (guests.Count == 0) return;
            var next = guests.IndexOf(CasHolder) + 1;
            if (next >= guests.Count) return;
            SetCasHolder(guests[next]);
        }

        /// <summary>
        /// The host takes control back, or the guest holding it gives it back.
        /// </summary>
        public static void CasTake()
        {
            if (Host != null) SetCasHolder(CoopProtocol.HostPlayerID);
            else if (Guest != null && CasMine) Guest.SendCasRelease(); //the host answers with the new holder
        }

        /// <summary>The name of the guest the host's "give control" would go to, or null if there is nobody to give it to.</summary>
        public static string CasNextName
        {
            get
            {
                if (Host == null) return null;
                var guests = Host.GuestIDs();
                var next = guests.IndexOf(CasHolder) + 1;
                if (next >= guests.Count) return null;
                var name = Host.GuestName(guests[next]);
                return string.IsNullOrEmpty(name) ? "the guest" : name;
            }
        }

        private static void SetCasHolder(uint holder)
        {
            if (Host == null || CasHolder == holder) return;
            CasHolder = holder;
            CasVersion++;
            if (Scene == CoopScene.CreateFamily && HasGuests) Host.SendToGuests(CoopFrameKind.Cas, PackCas());
        }

        /// <summary>
        /// A guest sent its version of the family. It counts only from the guest that holds control, and only
        /// if every Sim in it is one this game can build: the family is created in the host's game.
        /// </summary>
        private static void HostCasEdit(uint guestID, byte[] data)
        {
            if (guestID != CasHolder || Scene != CoopScene.CreateFamily) return;
            string problem = null;
            try
            {
                var family = CoopCasState.Unpack(data);
                if (family.Mode == CoopCasState.ModeFamilies)
                {
                    //the list of families is the host's alone
                    family.Mode = CoopCasState.ModeFamily;
                    data = family.Pack();
                }
                //only the Sims in the family are checked. One still being made is just shown, and its maker may
                //yet be flicking past heads this game lacks.
                foreach (var member in family.Members)
                {
                    if (!TS1CASScreen.LookAvailable(member))
                    {
                        problem = "The host's game does not have the head or body chosen for " +
                            (string.IsNullOrEmpty(member.Name) ? "that Sim" : member.Name) + ", so it was not added. Please pick another.";
                        break;
                    }
                }
            }
            catch (Exception)
            {
                problem = "The host could not read the family that was sent, so it was not changed.";
            }

            if (problem != null)
            {
                //put the guest's screen back to the family as the host has it, and say why
                Host.SendTo(guestID, CoopFrameKind.Cas, PackCas());
                using (var stream = new System.IO.MemoryStream())
                {
                    using (var writer = new System.IO.BinaryWriter(stream)) writer.Write(problem);
                    Host.SendTo(guestID, CoopFrameKind.Notice, stream.ToArray());
                }
                return;
            }
            CasFamily = data;
            CasVersion++;
            Host.SendToGuests(CoopFrameKind.Cas, PackCas(), guestID);
        }

        private static void ShowNotice(string text)
        {
            UIMobileAlert alert = null;
            alert = new UIMobileAlert(new UIAlertOptions()
            {
                Title = "Co-op",
                Message = text,
                Buttons = UIAlertButton.Ok((b) => alert.Close())
            });
            UIScreen.GlobalShowDialog(alert, true);
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
            guest.OnCas += (holder, family) =>
            {
                CasHolder = holder;
                CasFamily = family;
                CasVersion++;
            };
            guest.OnNotice += ShowNotice;
            guest.OnPointer += (player, x, y, shown) =>
            {
                if (player != MyID) SetPointer(player, (player == CoopProtocol.HostPlayerID) ? "Host" : "Guest", x, y, shown);
            };
            guest.OnClosed += (reason) =>
            {
                Console.WriteLine("COOP session ended: " + reason);
                EndGuest();
                ShowNotice(reason);
            };

            HostScene = CoopScene.None;
            HostArea = 4;
            HostHouse = 0;
            HostMoveIn = -1;
            WorldSeen = null;
            NeighbourhoodStale = false;
            HousesAwaited.Clear();
            Pointers.Clear();
            PointerShown = false;
            CasHolder = CoopProtocol.HostPlayerID;
            CasFamily = new CoopCasState().Pack();
            CasVersion++;
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
            Pointers.Clear();
            PointerShown = false;
            WorldSeen = null;
            ScreenSeen = null;
            CasHolder = CoopProtocol.HostPlayerID;
            CasVersion++;
            var screen = GameFacade.Screens?.CurrentUIScreen;
            if (screen is TS1GameScreen) ((TS1GameScreen)screen).CoopEnded();
            else
            {
                //no game screen to tidy up, but our own neighbourhood and objects still have to come back
                CoopWorld.Restore();
                CoopContentScanner.Restore();
                //from Create-a-Family, go back to the neighbourhood: ours again now
                (screen as TS1CASScreen)?.CoopLeave();
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
            if (Guest == null) return;
            var screen = GameFacade.Screens?.CurrentUIScreen;
            ScreenSeen = screen;
            if (Guest.PlayerID == 0) return; //still joining: nothing to follow yet, and the status line says how far along
            var game = screen as TS1GameScreen;
            var cas = screen as TS1CASScreen;

            if (HostScene == CoopScene.CreateFamily)
            {
                if (cas != null)
                {
                    Status = null; //the Create-a-Family screen says who is in control itself
                    return;
                }
                if (game == null) return;
                if (game.InLot) game.GuestLeaveLot();
                game.GuestHideNeighbourhood();
                if (!CoopWorld.Applied)
                {
                    //the host's neighbourhood comes first: its families and Sims are what the screen lists
                    Status = "The host is creating a family... (Esc to leave)";
                    return;
                }
                Status = null;
                ReportPointer(0, 0, false); //the neighbourhood screen goes away, and its pointer with it
                GameController.EnterCAS(); //our own Create-a-Family screen, which follows the host's family
                return;
            }
            if (cas != null)
            {
                cas.CoopLeave(); //back to the game screen, which is set up when it appears (see Update)
                return;
            }
            if (game == null) return;
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
