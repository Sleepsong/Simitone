using FSO.SimAntics;
using FSO.SimAntics.Engine.TSOTransaction;
using FSO.SimAntics.NetPlay.Drivers;
using FSO.SimAntics.NetPlay.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// The host's VM driver while a co-op session is open. It is FreeSO's server driver with the
    /// TS1 specific rules FreeSO does not have:
    ///  - the game speed of every tick is recorded, so guests can run each tick at the host's speed;
    ///  - guests do not get a Sim assigned (or sent off the lot) by the TSO join/leave commands;
    ///  - nobody can take a Sim that another connected player controls, and guests can only take family members.
    /// </summary>
    public class CoopServerDriver : VMServerDriver
    {
        /// <summary>IDs of the guests that are connected right now. Maintained by <see cref="CoopHost"/>.</summary>
        public readonly HashSet<uint> GuestIDs = new HashSet<uint>();

        private VM VM;
        private readonly List<short> PendingSpeeds = new List<short>();
        //tick packets are kept by the base driver to catch up joining players, so remember the speeds for each
        private readonly ConditionalWeakTable<byte[], short[]> SpeedsByPacket = new ConditionalWeakTable<byte[], short[]>();
        //Sims claimed by a command that has not run yet, so two players cannot take the same Sim in one tick
        private readonly Dictionary<short, uint> PendingClaims = new Dictionary<short, uint>();

        public CoopServerDriver(IVMTSOGlobalLink globalLink) : base(globalLink)
        {
            //subscribed first, so the speeds are stored before any other handler asks for them
            OnTickBroadcast += (msg, ignore) =>
            {
                SpeedsByPacket.AddOrUpdate(msg.Data, PendingSpeeds.ToArray());
                PendingSpeeds.Clear();
            };
        }

        /// <summary>
        /// The game speed the host ran each tick of this packet at, or null if it is not a tick packet (eg. a state sync).
        /// </summary>
        public short[] GetSpeeds(byte[] packet)
        {
            return SpeedsByPacket.TryGetValue(packet, out var speeds) ? speeds : null;
        }

        public override bool Tick(VM vm)
        {
            VM = vm;
            PendingSpeeds.Add((short)System.Math.Max(short.MinValue, System.Math.Min(short.MaxValue, vm.SpeedMultiplier)));
            var result = base.Tick(vm);
            PendingClaims.Clear(); //everything queued before this tick has now run
            return result;
        }

        public override void SendCommand(VMNetCommandBodyAbstract cmd)
        {
            if (CoopProtocol.IsGuestID(cmd.ActorUID) && (cmd is VMNetSimJoinCmd || cmd is VMNetSimLeaveCmd))
            {
                //in TS1 mode a join grabs the first family member (often the host's Sim) and a leave walks
                //the Sim off the lot. Guests pick a free Sim themselves and just let go of it when they leave.
                return;
            }

            if (cmd is VMNetChangeControlCmd control)
            {
                if (!CanTakeControl(control.ActorUID, control.TargetID)) return;
                PendingClaims[control.TargetID] = control.ActorUID;
            }

            base.SendCommand(cmd);
        }

        /// <summary>
        /// True if this player currently controls a Sim. Players that left keep their ID on the Sim
        /// they had until someone else takes it, so only connected players count.
        /// </summary>
        public bool IsConnectedPlayer(uint id)
        {
            if (id == 0) return false;
            return (VM != null && id == VM.MyUID) || GuestIDs.Contains(id);
        }

        public bool CanTakeControl(uint actor, short targetID)
        {
            if (VM == null) return true;
            var target = VM.GetObjectById(targetID) as VMAvatar;
            if (target == null) return true; //the command rejects this itself

            if (target.PersistID != actor && IsConnectedPlayer(target.PersistID)) return false;
            if (PendingClaims.TryGetValue(targetID, out var claimer) && claimer != actor) return false;

            if (CoopProtocol.IsGuestID(actor))
            {
                var family = VM.TS1State?.CurrentFamily;
                if (family == null || !family.RuntimeSubset.Contains(target.Object.OBJ.GUID)) return false;
            }
            return true;
        }
    }
}
