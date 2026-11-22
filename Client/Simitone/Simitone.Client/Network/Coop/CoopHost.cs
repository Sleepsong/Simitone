using FSO.SimAntics.NetPlay.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// The host's side of a co-op session: accepts guests and relays between them and the lot's driver.
    /// The session outlives any one lot - when the host moves to another lot, <see cref="Attach"/> hands
    /// the connected guests to the new lot's driver and they are sent its state.
    /// Everything except accepting connections and sending object files happens on the game thread, in <see cref="Update"/>.
    ///
    /// A guest joins in steps: it says Hello, is sent a Welcome with this game's object files listed, asks
    /// for the files it lacks (ContentRequest), and says Ready once it has them. Only then is it told where the
    /// host is (<see cref="Greeting"/>) and, if that is on a lot, given the lot.
    /// </summary>
    public class CoopHost
    {
        public int Port { get; private set; }

        /// <summary>The guests that are playing. Guests still fetching objects are not counted.</summary>
        public int GuestCount { get { return Guests.Count; } }

        /// <summary>This game's version, which each guest compares with its own.</summary>
        public string GameVersion = SimitoneVersion.Current;

        /// <summary>
        /// This game's object files, which each guest compares with its own and fetches what it lacks from.
        /// Guests that arrive before it is ready wait for it.
        /// </summary>
        public Task<CoopManifest> Content = Task.FromResult(new CoopManifest());

        /// <summary>How many object files have been sent to guests.</summary>
        public int ContentFilesSent { get { return SentCount; } }

        /// <summary>
        /// The frames to send a guest the moment it is ready, before anything about a lot: where the host is, and
        /// its neighbourhood if that is what it is looking at.
        /// </summary>
        public Func<List<KeyValuePair<CoopFrameKind, byte[]>>> Greeting;

        /// <summary>
        /// Reads one of this game's house files by name, for guests that ask for it. Called off the game thread.
        /// </summary>
        public Func<string, byte[]> ReadHouse;

        /// <summary>A guest asked for a game speed (0 pause, 1-3 as on the clock).</summary>
        public event Action<int> OnSpeedRequest;
        /// <summary>A guest joined (true) or left (false). The string is their name.</summary>
        public event Action<string, bool> OnGuestChange;
        /// <summary>A guest is being sent this many object files, of this many bytes. The string is their name.</summary>
        public event Action<string, int, long> OnContentRequest;

        private class GuestState
        {
            public VMNetClient Client;
            public bool Ready;           //has its objects and is in Guests
            public bool AskedForContent; //one request per guest
        }

        //a guest is only sent more object files while less than this is waiting to go out to it
        private const long MaxQueuedContent = 8 * 1024 * 1024;

        private TcpListener Listener;
        private volatile bool Running;
        private readonly ConcurrentQueue<CoopFrame> Incoming = new ConcurrentQueue<CoopFrame>();
        private readonly List<VMNetClient> Guests = new List<VMNetClient>();
        private readonly List<CoopConnection> Connections = new List<CoopConnection>(); //every guest that got as far as Hello
        private readonly List<KeyValuePair<CoopConnection, byte[]>> WaitingHellos = new List<KeyValuePair<CoopConnection, byte[]>>();
        private byte[] PackedContent;
        private CoopServerDriver Driver;
        private uint NextGuestID = CoopProtocol.FirstGuestID;
        private int SentCount;

        public void Start(int port)
        {
            Port = port;
            Listener = new TcpListener(IPAddress.Any, port);
            Listener.Start();
            Running = true;
            new Thread(AcceptLoop) { IsBackground = true, Name = "Coop accept" }.Start();
        }

        private void AcceptLoop()
        {
            while (Running)
            {
                try
                {
                    var socket = Listener.AcceptTcpClient();
                    new CoopConnection(socket, Incoming); //says Hello next, which Update handles
                }
                catch (Exception)
                {
                    if (!Running) return;
                }
            }
        }

        public void Shutdown()
        {
            Running = false;
            try { Listener?.Stop(); } catch { }
            Attach(null);
            foreach (var conn in Connections.ToArray()) conn.Close();
            foreach (var waiting in WaitingHellos) waiting.Key.Close();
            Connections.Clear();
            WaitingHellos.Clear();
            Guests.Clear();
        }

        /// <summary>
        /// Makes this the driver guests play on, or null while the host is not on a lot.
        /// </summary>
        public void Attach(CoopServerDriver driver)
        {
            if (Driver != null)
            {
                Driver.OnTickBroadcast -= BroadcastTicks;
                Driver.OnDirectMessage -= SendDirect;
                Driver.OnDropClient -= DropClient;
            }
            Driver = driver;
            if (driver == null) return;

            driver.OnTickBroadcast += BroadcastTicks;
            driver.OnDirectMessage += SendDirect;
            driver.OnDropClient += DropClient;
            foreach (var guest in Guests) AddToDriver(guest);
        }

        private void AddToDriver(VMNetClient guest)
        {
            guest.HadAvatar = false;
            guest.InactivityTicks = 0;
            Driver.GuestIDs.Add(guest.PersistID);
            Driver.ConnectClient(guest); //queues the lot state for them
        }

        public void Update()
        {
            if (WaitingHellos.Count > 0 && Content.IsCompleted)
            {
                var waiting = WaitingHellos.ToArray();
                WaitingHellos.Clear();
                foreach (var hello in waiting)
                {
                    if (!hello.Key.Closed) HandleHello(hello.Key, hello.Value);
                }
            }

            while (Incoming.TryDequeue(out var frame))
            {
                var conn = frame.Connection;
                var state = conn.Tag as GuestState;
                var guest = (state != null && state.Ready) ? state.Client : null;
                switch (frame.Kind)
                {
                    case CoopFrameKind.Hello:
                        if (state == null) HandleHello(conn, frame.Data);
                        break;
                    case CoopFrameKind.ContentRequest:
                        if (state != null && !state.Ready && !state.AskedForContent)
                        {
                            state.AskedForContent = true;
                            HandleContentRequest(conn, state, frame.Data);
                        }
                        break;
                    case CoopFrameKind.Ready:
                        if (state != null && !state.Ready)
                        {
                            state.Ready = true;
                            Guests.Add(state.Client);
                            SendPlayers();
                            var greeting = Greeting?.Invoke();
                            if (greeting != null)
                            {
                                foreach (var item in greeting) conn.Send(item.Key, item.Value);
                            }
                            if (Driver != null) AddToDriver(state.Client);
                            OnGuestChange?.Invoke(state.Client.AvatarState.Name, true);
                        }
                        break;
                    case CoopFrameKind.HouseRequest:
                        if (guest != null) HandleHouseRequest(conn, frame.Data);
                        break;
                    case CoopFrameKind.Command:
                        if (guest != null && Driver != null)
                            Driver.HandleMessage(guest, new VMNetMessage(VMNetMessageType.Command, frame.Data));
                        break;
                    case CoopFrameKind.SpeedRequest:
                        if (guest != null && frame.Data.Length == 1 && frame.Data[0] <= 3)
                            OnSpeedRequest?.Invoke(frame.Data[0]);
                        break;
                    case CoopFrameKind.Closed:
                        Connections.Remove(conn);
                        if (guest != null && Guests.Remove(guest))
                        {
                            if (Driver != null)
                            {
                                Driver.GuestIDs.Remove(guest.PersistID);
                                Driver.DisconnectClient(guest);
                            }
                            SendPlayers();
                            OnGuestChange?.Invoke(guest.AvatarState.Name, false);
                        }
                        break;
                }
            }
        }

        private void HandleHello(CoopConnection conn, byte[] data)
        {
            int version;
            string name;
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(data)))
                {
                    version = reader.ReadInt32();
                    name = reader.ReadString();
                }
            }
            catch (Exception)
            {
                conn.Close();
                return;
            }

            if (version != CoopProtocol.Version)
            {
                Reject(conn, "This game's co-op version (" + version + ") does not match the host's (" + CoopProtocol.Version + ").");
                return;
            }
            if (!Content.IsCompleted)
            {
                //still fingerprinting our objects. Update comes back to this guest when that is done
                WaitingHellos.Add(new KeyValuePair<CoopConnection, byte[]>(conn, data));
                return;
            }
            if (Content.IsFaulted || Content.IsCanceled)
            {
                Reject(conn, "The host could not read its own game's objects, so nobody can join it.");
                return;
            }
            if (PackedContent == null) PackedContent = Content.Result.Pack();
            if (name.Length > 32) name = name.Substring(0, 32);

            var id = NextGuestID++;
            var guest = new VMNetClient
            {
                PersistID = id,
                RemoteIP = conn.RemoteAddress,
                NetHandle = conn,
                AvatarState = new VMNetAvatarPersistState { Name = name, PersistID = id }
            };
            conn.Tag = new GuestState { Client = guest };
            Connections.Add(conn);

            //the guest answers with ContentRequest and then Ready, or leaves if it cannot play here
            conn.Send(CoopFrameKind.Welcome, CoopCompat.PackWelcome(id, GameVersion, PackedContent));
        }

        private void HandleContentRequest(CoopConnection conn, GuestState state, byte[] data)
        {
            var files = Content.Result.Files;
            var wanted = new List<CoopContentFile>();
            var indices = new List<int>();
            long total = 0;
            try
            {
                if (data.Length < 4) throw new InvalidDataException();
                var count = BitConverter.ToInt32(data, 0);
                if (count < 0 || count > files.Count || data.Length != 4 + count * 4) throw new InvalidDataException();
                var seen = new HashSet<int>();
                for (int i = 0; i < count; i++)
                {
                    var index = BitConverter.ToInt32(data, 4 + i * 4);
                    if (index < 0 || index >= files.Count || !seen.Add(index)) throw new InvalidDataException();
                    indices.Add(index);
                    wanted.Add(files[index]);
                    total += files[index].Size;
                }
            }
            catch (Exception)
            {
                conn.Close();
                return;
            }
            OnContentRequest?.Invoke(state.Client.AvatarState.Name, wanted.Count, total);

            //files are read and queued on their own thread, no faster than the guest's connection takes them
            new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < wanted.Count; i++)
                    {
                        while (conn.QueuedBytes > MaxQueuedContent && !conn.Closed) Thread.Sleep(5);
                        if (conn.Closed || !Running) return;
                        conn.Send(CoopFrameKind.ContentFile, BitConverter.GetBytes(indices[i]), wanted[i].ReadBytes());
                        Interlocked.Increment(ref SentCount);
                    }
                }
                catch (Exception e)
                {
                    Reject(conn, "The host could not send one of its objects.\n\n" + e.Message);
                }
            })
            { IsBackground = true, Name = "Coop content" }.Start();
        }

        private void HandleHouseRequest(CoopConnection conn, byte[] data)
        {
            var names = new List<string>();
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(data)))
                {
                    var count = reader.ReadInt32();
                    if (count < 0 || count > 512) throw new InvalidDataException();
                    for (int i = 0; i < count; i++)
                    {
                        var name = reader.ReadString();
                        if (!CoopWorld.IsHouseName(name)) throw new InvalidDataException();
                        names.Add(name);
                    }
                }
            }
            catch (Exception)
            {
                conn.Close();
                return;
            }
            var read = ReadHouse;
            if (read == null || names.Count == 0) return;

            new Thread(() =>
            {
                foreach (var name in names)
                {
                    while (conn.QueuedBytes > MaxQueuedContent && !conn.Closed) Thread.Sleep(5);
                    if (conn.Closed || !Running) return;
                    byte[] header;
                    byte[] bytes;
                    try
                    {
                        bytes = read(name);
                        using (var stream = new MemoryStream())
                        {
                            using (var writer = new BinaryWriter(stream)) writer.Write(name);
                            header = stream.ToArray();
                        }
                    }
                    catch (Exception)
                    {
                        //gone or being saved. An empty file tells the guest to keep what it has
                        using (var stream = new MemoryStream())
                        {
                            using (var writer = new BinaryWriter(stream)) writer.Write(name);
                            header = stream.ToArray();
                        }
                        bytes = null;
                    }
                    conn.Send(CoopFrameKind.HouseFile, header, bytes);
                }
            })
            { IsBackground = true, Name = "Coop houses" }.Start();
        }

        /// <summary>
        /// Sends a frame to every guest that is playing.
        /// </summary>
        public void SendToGuests(CoopFrameKind kind, byte[] data)
        {
            foreach (var guest in Guests) ((CoopConnection)guest.NetHandle).Send(kind, data);
        }

        /// <summary>
        /// Tells every guest who is in the session, so each knows which Sims are spoken for.
        /// </summary>
        private void SendPlayers()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Guests.Count + 1);
                    writer.Write(CoopProtocol.HostPlayerID);
                    foreach (var guest in Guests) writer.Write(guest.PersistID);
                }
                var data = stream.ToArray();
                foreach (var guest in Guests) ((CoopConnection)guest.NetHandle).Send(CoopFrameKind.Players, data);
            }
        }

        private void Reject(CoopConnection conn, string reason)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream)) writer.Write(reason);
                conn.Send(CoopFrameKind.Reject, stream.ToArray());
            }
            //give the writer thread a moment to send the reason before the socket closes
            new Thread(() => { Thread.Sleep(1000); conn.Close(); }) { IsBackground = true }.Start();
        }

        private void SendTicks(VMNetClient guest, byte[] packet)
        {
            var conn = guest.NetHandle as CoopConnection;
            if (conn == null) return; //the host's own local client

            var speeds = Driver?.GetSpeeds(packet);
            if (speeds == null && Driver?.SyncWorld != null)
            {
                //not a packet of ticks, so it is a lot state. The neighbourhood it belongs to goes first.
                conn.Send(CoopFrameKind.World, Driver.SyncWorld);
            }
            var count = (speeds == null || speeds.Length > 255) ? 0 : speeds.Length;
            var header = new byte[1 + count * 2];
            header[0] = (byte)count;
            for (int i = 0; i < count; i++) BitConverter.TryWriteBytes(new Span<byte>(header, 1 + i * 2, 2), speeds[i]);
            conn.Send(CoopFrameKind.Tick, header, packet);
        }

        private void BroadcastTicks(VMNetMessage msg, HashSet<VMNetClient> ignore)
        {
            foreach (var guest in Guests)
            {
                if (ignore != null && ignore.Contains(guest)) continue; //still waiting for the lot state
                SendTicks(guest, msg.Data);
            }
        }

        private void SendDirect(VMNetClient target, VMNetMessage msg)
        {
            if (msg.Type == VMNetMessageType.BroadcastTick) SendTicks(target, msg.Data);
            else (target.NetHandle as CoopConnection)?.Send(CoopFrameKind.Direct, msg.Data);
        }

        private void DropClient(VMNetClient client)
        {
            var conn = client.NetHandle as CoopConnection;
            if (conn == null) return; //the host's own local client

            if (client.FatalDCMessage != null)
            {
                conn.Close(); //sent something unreadable
                return;
            }
            //FreeSO drops players whose Sim left the lot and players that idle. In co-op a Sim leaving
            //(for work, say) is normal play and friends are welcome to idle, so keep them.
            client.HadAvatar = false;
            client.InactivityTicks = 0;
        }
    }
}
