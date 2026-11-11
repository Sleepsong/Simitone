using FSO.SimAntics;
using FSO.SimAntics.NetPlay;
using FSO.SimAntics.NetPlay.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// A guest's VM driver. It runs the ticks the host sends, in order, each at the game speed the host
    /// ran it at. (FreeSO's client driver assumes the speed never changes; in TS1 the clock only
    /// advances while the game is running, so a guest with the wrong speed would drift out of sync.)
    /// </summary>
    public class CoopClientDriver : VMNetDriver
    {
        private struct BufferedTick
        {
            public VMNetTick Tick;
            public short? Speed;
            public byte[] World; //the host's neighbourhood, to load before this tick (which loads a lot state)
        }

        private const int CATCH_UP_TICKS = 12;   //more than this buffered: run two ticks at a time
        private const int FAST_FORWARD_TICKS = 90; //more than this buffered: run as many as fit in a frame
        private const int MAX_TICK_MS = 50;

        private readonly Queue<BufferedTick> TickBuffer = new Queue<BufferedTick>();
        private bool ExecutedAnything;

        /// <summary>Fired with a serialized VMNetCommand that should go to the host.</summary>
        public event Action<byte[]> OnClientCommand;

        public CoopClientDriver()
        {
            GlobalLink = null; //as in FreeSO, only the host performs transactions
        }

        public override void SendCommand(VMNetCommandBodyAbstract cmd)
        {
            byte[] data;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    new VMNetCommand(cmd).SerializeInto(writer);
                }
                data = stream.ToArray();
            }
            OnClientCommand?.Invoke(data);
        }

        /// <summary>
        /// Queues a packet of ticks from the host. speeds has one entry per tick, or is empty when unknown.
        /// world is the packed neighbourhood to load before the first of them, if any.
        /// </summary>
        public void EnqueueTicks(byte[] data, short[] speeds, byte[] world = null)
        {
            var list = new VMNetTickList();
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(data)))
                {
                    list.Deserialize(reader);
                }
            }
            catch (Exception e)
            {
                CloseReason = VMCloseNetReason.NetException;
                CloseString = "The host sent game data this copy of the game could not read: \n\n" + e.ToString();
                Shutdown();
                return;
            }

            var hasSpeeds = speeds != null && speeds.Length == list.Ticks.Count;
            for (int i = 0; i < list.Ticks.Count; i++)
            {
                list.Ticks[i].ImmediateMode = list.ImmediateMode;
                TickBuffer.Enqueue(new BufferedTick
                {
                    Tick = list.Ticks[i],
                    Speed = hasSpeeds ? speeds[i] : (short?)null,
                    World = (i == 0) ? world : null
                });
            }
        }

        /// <summary>Runs a command the host sent to this guest alone.</summary>
        public void HandleDirect(VM vm, byte[] data)
        {
            var cmd = new VMNetCommand();
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(data)))
                {
                    cmd.Deserialize(reader);
                }
                cmd.Command.Execute(vm, vm.GetAvatarByPersist(cmd.Command.ActorUID));
            }
            catch (Exception e)
            {
                CloseReason = VMCloseNetReason.NetExceptionDirect;
                CloseString = "A message from the host could not be run: \n\n" + e.ToString();
                Shutdown();
            }
        }

        public override bool Tick(VM vm)
        {
            int toRun;
            if (TickBuffer.Count > FAST_FORWARD_TICKS) toRun = int.MaxValue;
            else if (TickBuffer.Count > CATCH_UP_TICKS) toRun = 2;
            else toRun = 1;

            var timer = Stopwatch.StartNew();
            for (int i = 0; i < toRun && TickBuffer.Count > 0; i++)
            {
                var next = TickBuffer.Dequeue();
                if (next.World != null)
                {
                    try
                    {
                        CoopWorld.Unpack(next.World).Apply();
                    }
                    catch (Exception e)
                    {
                        CloseReason = VMCloseNetReason.NetException;
                        CloseString = "The host's neighbourhood could not be loaded: \n\n" + e.Message;
                        Shutdown();
                        return false;
                    }
                }
                if (next.Speed.HasValue) vm.SpeedMultiplier = next.Speed.Value;
                ExecutedAnything = true;
                var sync = (next.Tick.Commands.Count > 0) ? next.Tick.Commands[0].Command as VMStateSyncCmd : null;
                if (sync != null && sync.Run)
                {
                    //a lot state. Load it here rather than through the command: after loading, the command goes
                    //on to rebuild the surrounding lots from TSO city data, which a TS1 lot does not have.
                    CurrentTick = next.Tick.TickID;
                    if (CurrentTick > GreatestTick) GreatestTick = CurrentTick;
                    DesyncTick = 0;
                    if (vm.TS1State != null)
                    {
                        //loading keeps the family the VM already has, not the one in the state
                        var family = (sync.State.PlatformState as FSO.SimAntics.Model.TS1Platform.VMTS1LotState)?.CurrentFamily;
                        vm.TS1State.CurrentFamily = CoopWorld.ResolveFamily(family);
                    }
                    vm.Load(sync.State);
                    LastTick = next.Tick.TickID;
                }
                else InternalTick(vm, next.Tick);
                if (vm.FSOVAsyncLoading) return false; //a lot state is still loading
                if (timer.ElapsedMilliseconds > MAX_TICK_MS) break;
            }

            return ExecutedAnything && vm.Context.Ready && TickBuffer.Count <= FAST_FORWARD_TICKS;
        }

        public override string GetUserIP(uint uid)
        {
            return "remote";
        }

        public override void SendDirectCommand(uint pid, VMNetCommandBodyAbstract cmd)
        {
            //only the host can do this
        }
    }
}
