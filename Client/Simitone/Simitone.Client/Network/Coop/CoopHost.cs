using FSO.SimAntics.NetPlay.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// The host's side of a co-op session: accepts guests and relays between them and the lot's driver.
    /// The session outlives any one lot - when the host moves to another lot, <see cref="Attach"/> hands
    /// the connected guests to the new lot's driver and they are sent its state.
    /// Everything except accepting connections happens on the game thread, in <see cref="Update"/>.
    /// </summary>
    public class CoopHost
    {
        public int Port { get; private set; }
        public int GuestCount { get { return Guests.Count; } }

        /// <summary>A guest asked for a game speed (0 pause, 1-3 as on the clock).</summary>
        public event Action<int> OnSpeedRequest;
        /// <summary>A guest joined (true) or left (false). The string is their name.</summary>
        public event Action<string, bool> OnGuestChange;

        private TcpListener Listener;
        private volatile bool Running;
        private readonly ConcurrentQueue<CoopFrame> Incoming = new ConcurrentQueue<CoopFrame>();
        private readonly List<VMNetClient> Guests = new List<VMNetClient>();
        private CoopServerDriver Driver;
        private uint NextGuestID = CoopProtocol.FirstGuestID;

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
            foreach (var guest in Guests) ((CoopConnection)guest.NetHandle).Close();
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
            while (Incoming.TryDequeue(out var frame))
            {
                var conn = frame.Connection;
                var guest = conn.Tag as VMNetClient;
                switch (frame.Kind)
                {
                    case CoopFrameKind.Hello:
                        if (guest == null) HandleHello(conn, frame.Data);
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
                        if (guest != null && Guests.Remove(guest))
                        {
                            if (Driver != null)
                            {
                                Driver.GuestIDs.Remove(guest.PersistID);
                                Driver.DisconnectClient(guest);
                            }
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
            if (name.Length > 32) name = name.Substring(0, 32);

            var id = NextGuestID++;
            var guest = new VMNetClient
            {
                PersistID = id,
                RemoteIP = conn.RemoteAddress,
                NetHandle = conn,
                AvatarState = new VMNetAvatarPersistState { Name = name, PersistID = id }
            };
            conn.Tag = guest;
            Guests.Add(guest);

            conn.Send(CoopFrameKind.Welcome, BitConverter.GetBytes(id));
            if (Driver != null) AddToDriver(guest);
            OnGuestChange?.Invoke(name, true);
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
