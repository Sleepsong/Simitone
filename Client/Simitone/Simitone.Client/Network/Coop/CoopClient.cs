using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// A guest's side of a co-op session. Connects to a host, then feeds the host's ticks to the
    /// guest's driver and the guest's commands to the host. Polled on the game thread via <see cref="Update"/>.
    /// </summary>
    public class CoopClient
    {
        public readonly CoopClientDriver Driver = new CoopClientDriver();

        /// <summary>This guest's player ID in the VM. 0 until the host has welcomed us.</summary>
        public uint PlayerID { get; private set; }

        /// <summary>The host accepted us; PlayerID is set and the lot state will follow.</summary>
        public event Action OnWelcome;
        /// <summary>The session ended. The string says why, and is empty if we left ourselves.</summary>
        public event Action<string> OnClosed;
        /// <summary>A command the host sent to this guest alone (a serialized VMNetCommand).</summary>
        public event Action<byte[]> OnDirect;

        private readonly ConcurrentQueue<CoopFrame> Incoming = new ConcurrentQueue<CoopFrame>();
        private volatile CoopConnection Connection;
        private bool Ended;

        /// <summary>This game's version and objects, compared with the host's on joining. See <see cref="CoopCompat"/>.</summary>
        public string GameVersion = SimitoneVersion.Current;
        public Func<IEnumerable<uint>> GameObjects = () => new uint[0];
        private byte[] PendingWorld; //the neighbourhood for the lot state in the next Tick frame
        private string PendingEnd; //set by the driver mid-tick; the session ends on the next Update

        public CoopClient()
        {
            Driver.OnClientCommand += (data) => Connection?.Send(CoopFrameKind.Command, data);
            Driver.OnShutdown += (reason) =>
            {
                if (reason == FSO.SimAntics.NetPlay.VMCloseNetReason.LeaveLot) Disconnect();
                else PendingEnd = Driver.CloseString ?? "The game lost sync with the host.";
            };
        }

        /// <summary>
        /// Parses "host" or "host:port" (or "[v6]:port").
        /// </summary>
        public static void ParseAddress(string address, out string host, out int port)
        {
            host = address.Trim();
            port = CoopProtocol.DefaultPort;
            var colon = host.LastIndexOf(':');
            var isBareV6 = host.IndexOf(':') != colon && !host.StartsWith("[");
            if (colon > 0 && !isBareV6 && int.TryParse(host.Substring(colon + 1), out var parsed))
            {
                port = parsed;
                host = host.Substring(0, colon);
            }
            host = host.Trim('[', ']');
        }

        public void Connect(string address, string playerName)
        {
            ParseAddress(address, out var host, out var port);
            new Thread(() =>
            {
                try
                {
                    var socket = new TcpClient();
                    socket.Connect(host, port);
                    var conn = new CoopConnection(socket, Incoming);
                    Connection = conn; //before Hello, so the reply always finds it
                    using (var stream = new MemoryStream())
                    {
                        using (var writer = new BinaryWriter(stream))
                        {
                            writer.Write(CoopProtocol.Version);
                            writer.Write(playerName ?? "");
                        }
                        conn.Send(CoopFrameKind.Hello, stream.ToArray());
                    }
                }
                catch (Exception e)
                {
                    Incoming.Enqueue(new CoopFrame
                    {
                        Kind = CoopFrameKind.Closed,
                        Data = Encoding.UTF8.GetBytes("Could not connect to " + host + ":" + port + ".\n\n" + e.Message)
                    });
                }
            })
            { IsBackground = true, Name = "Coop connect" }.Start();
        }

        public void RequestSpeed(int speed)
        {
            Connection?.Send(CoopFrameKind.SpeedRequest, new byte[] { (byte)speed });
        }

        /// <summary>Leaves the session. OnClosed is not fired for this.</summary>
        public void Disconnect()
        {
            Ended = true;
            Connection?.Close();
        }

        public void Update()
        {
            if (PendingEnd != null)
            {
                var reason = PendingEnd;
                PendingEnd = null;
                End(reason);
            }
            while (!Ended && Incoming.TryDequeue(out var frame))
            {
                switch (frame.Kind)
                {
                    case CoopFrameKind.Welcome:
                        if (PlayerID != 0) break;
                        uint id;
                        string problem;
                        try
                        {
                            CoopCompat.ParseWelcome(frame.Data, out id, out var hostVersion, out var hostObjects);
                            problem = CoopCompat.FindProblem(hostVersion, hostObjects, GameVersion, GameObjects());
                        }
                        catch (Exception)
                        {
                            End("The host sent a reply this game could not read.");
                            break;
                        }
                        if (problem != null)
                        {
                            End(problem);
                            break;
                        }
                        PlayerID = id;
                        OnWelcome?.Invoke();
                        break;
                    case CoopFrameKind.Reject:
                        string reason;
                        try
                        {
                            using (var reader = new BinaryReader(new MemoryStream(frame.Data))) reason = reader.ReadString();
                        }
                        catch (Exception) { reason = "The host refused the connection."; }
                        End(reason);
                        break;
                    case CoopFrameKind.Tick:
                        if (PlayerID == 0 || frame.Data.Length < 1) break;
                        var count = frame.Data[0];
                        var headerSize = 1 + count * 2;
                        if (frame.Data.Length < headerSize) break;
                        var speeds = new short[count];
                        for (int i = 0; i < count; i++) speeds[i] = BitConverter.ToInt16(frame.Data, 1 + i * 2);
                        var ticks = new byte[frame.Data.Length - headerSize];
                        Buffer.BlockCopy(frame.Data, headerSize, ticks, 0, ticks.Length);
                        Driver.EnqueueTicks(ticks, speeds, PendingWorld);
                        PendingWorld = null;
                        break;
                    case CoopFrameKind.World:
                        if (PlayerID != 0) PendingWorld = frame.Data;
                        break;
                    case CoopFrameKind.Direct:
                        if (PlayerID != 0) OnDirect?.Invoke(frame.Data);
                        break;
                    case CoopFrameKind.Closed:
                        var text = Encoding.UTF8.GetString(frame.Data);
                        End(text == "" ? "Connection lost." : text);
                        break;
                }
            }
        }

        private void End(string reason)
        {
            if (Ended) return;
            Ended = true;
            Connection?.Close();
            OnClosed?.Invoke(reason);
        }
    }
}
