using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// Wire format for co-op play. Every frame is [int32 length][byte kind][payload], little endian,
    /// where length counts the kind byte and the payload.
    /// </summary>
    public enum CoopFrameKind : byte
    {
        //guest -> host
        Hello = 1,        //int32 protocol version, string player name
        Command = 2,      //a serialized VMNetCommand
        SpeedRequest = 3, //byte speed (0 pause, 1-3 as on the clock)
        ContentRequest = 4, //int32 n, int32[n] indices into the host's manifest of object files to send
        Ready = 5,        //empty: the guest has what it needs and can be given the lot
        HouseRequest = 6, //int32 n, string[n] names of house files to send

        //host -> guest
        Welcome = 16,     //uint32 the guest's player ID in the VM, string host game version, int32 n, byte[n] a packed CoopManifest
        Reject = 17,      //string reason
        Tick = 18,        //byte n, int16[n] game speed for each tick (n is 0 when unknown), then a serialized VMNetTickList
        Direct = 19,      //a serialized VMNetCommand, run immediately by this guest only
        Players = 21,     //int32 n, uint32[n] the player IDs of everyone in the session, the host's first
        ContentFile = 22, //int32 index into the host's manifest, then that file's bytes
        Lobby = 23,       //a packed CoopWorld: the host's neighbourhood as it is now, for the neighbourhood screen
        Scene = 24,       //byte CoopScene where the host is, uint16 area, int32 house being viewed (0 none), int32 family being moved in (-1 none)
        HouseFile = 25,   //string name, then that house file's bytes
        World = 20,       //a packed CoopWorld: the host's neighbourhood as of the lot state in the Tick frame that follows

        //never sent: queued locally when a connection ends. payload is a UTF8 reason, possibly empty
        Closed = 255
    }

    /// <summary>
    /// Which part of the game the host is in. Guests follow.
    /// </summary>
    public enum CoopScene : byte
    {
        None = 0,
        Neighbourhood = 1, //choosing a house. The area and the house being looked at say where
        Lot = 2,           //playing a lot: its state and ticks follow
        CreateFamily = 3   //in Create-a-Family
    }

    public struct CoopFrame
    {
        public CoopConnection Connection;
        public CoopFrameKind Kind;
        public byte[] Data;
    }

    public static class CoopProtocol
    {
        public const int Version = 7;
        public const int DefaultPort = 37565;
        public const int MaxFrameSize = 64 * 1024 * 1024; //lot states are a few MB at most

        /// <summary>The host's player ID in the VM (what TS1GameScreen sets as vm.MyUID).</summary>
        public const uint HostPlayerID = 65537;

        /// <summary>The first player ID given to a guest. The host's own IDs (1 and 65537) are well below it.</summary>
        public const uint FirstGuestID = 0x20000;

        public static bool IsGuestID(uint id)
        {
            return id >= FirstGuestID && id < uint.MaxValue - 1;
        }
    }

    /// <summary>
    /// One TCP connection. Frames are read and written on background threads, so the game thread never
    /// blocks on a slow peer. Received frames go into the shared queue the owner polls once per frame.
    /// </summary>
    public class CoopConnection
    {
        public readonly string RemoteAddress;
        public object Tag; //the owner's per-connection state

        private readonly TcpClient Socket;
        private readonly NetworkStream Stream;
        private readonly ConcurrentQueue<CoopFrame> Incoming;
        private readonly BlockingCollection<byte[]> Outgoing = new BlockingCollection<byte[]>();
        private int ClosedFlag;
        private long QueuedByteCount;

        public bool Closed { get { return ClosedFlag != 0; } }

        /// <summary>Bytes handed to Send that have not been written to the socket yet.</summary>
        public long QueuedBytes { get { return Interlocked.Read(ref QueuedByteCount); } }

        public CoopConnection(TcpClient socket, ConcurrentQueue<CoopFrame> incoming)
        {
            Socket = socket;
            Socket.NoDelay = true;
            Stream = socket.GetStream();
            Incoming = incoming;
            try { RemoteAddress = socket.Client.RemoteEndPoint.ToString(); }
            catch { RemoteAddress = "unknown"; }

            new Thread(ReadLoop) { IsBackground = true, Name = "Coop read" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "Coop write" }.Start();
        }

        public void Send(CoopFrameKind kind, byte[] payload)
        {
            Send(kind, payload, null);
        }

        /// <summary>Sends a frame whose payload is header followed by body (either can be null).</summary>
        public void Send(CoopFrameKind kind, byte[] header, byte[] body)
        {
            if (Closed) return;
            var headerLen = header?.Length ?? 0;
            var bodyLen = body?.Length ?? 0;
            var frame = new byte[5 + headerLen + bodyLen];
            BitConverter.TryWriteBytes(new Span<byte>(frame, 0, 4), 1 + headerLen + bodyLen);
            frame[4] = (byte)kind;
            if (headerLen > 0) Buffer.BlockCopy(header, 0, frame, 5, headerLen);
            if (bodyLen > 0) Buffer.BlockCopy(body, 0, frame, 5 + headerLen, bodyLen);
            Interlocked.Add(ref QueuedByteCount, frame.Length);
            try { Outgoing.Add(frame); }
            catch (InvalidOperationException) { } //closed meanwhile
        }

        public void Close(string reason = "")
        {
            if (Interlocked.Exchange(ref ClosedFlag, 1) != 0) return;
            try { Outgoing.CompleteAdding(); } catch { }
            try { Socket.Close(); } catch { }
            Incoming.Enqueue(new CoopFrame
            {
                Connection = this,
                Kind = CoopFrameKind.Closed,
                Data = System.Text.Encoding.UTF8.GetBytes(reason ?? "")
            });
        }

        private void ReadLoop()
        {
            try
            {
                var lengthBytes = new byte[4];
                while (!Closed)
                {
                    ReadExactly(lengthBytes, 4);
                    var length = BitConverter.ToInt32(lengthBytes, 0);
                    if (length < 1 || length > CoopProtocol.MaxFrameSize) throw new IOException("Bad frame size " + length);
                    var body = new byte[length];
                    ReadExactly(body, length);

                    var kind = (CoopFrameKind)body[0];
                    if (kind == CoopFrameKind.Closed) continue; //local only, never accepted from the wire
                    var data = new byte[length - 1];
                    Buffer.BlockCopy(body, 1, data, 0, length - 1);
                    Incoming.Enqueue(new CoopFrame { Connection = this, Kind = kind, Data = data });
                }
            }
            catch (Exception)
            {
                //fall through to close
            }
            Close("Connection lost.");
        }

        private void ReadExactly(byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = Stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        private void WriteLoop()
        {
            try
            {
                foreach (var frame in Outgoing.GetConsumingEnumerable())
                {
                    Stream.Write(frame, 0, frame.Length);
                    Interlocked.Add(ref QueuedByteCount, -frame.Length);
                }
            }
            catch (Exception)
            {
                //fall through to close
            }
            Close("Connection lost.");
        }
    }
}
