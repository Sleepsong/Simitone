using FSO.SimAntics;
using FSO.SimAntics.Engine.TSOTransaction;
using FSO.SimAntics.NetPlay.Drivers;
using FSO.SimAntics.NetPlay.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        /// <summary>
        /// The host's neighbourhood (a packed <see cref="CoopWorld"/>) as it was when the lot state that
        /// joining and resyncing guests are sent was taken. Null if it could not be captured.
        /// </summary>
        public byte[] SyncWorld { get; private set; }

        //the base driver starts a new tick history whenever it takes a lot state, and gives no other sign of it
        private static readonly FieldInfo SyncHistoryField = typeof(VMServerDriver).GetField("TicksSinceSync", BindingFlags.NonPublic | BindingFlags.Instance);
        private object LastSyncHistory;
        private static readonly FieldInfo LastSyncField = typeof(VMServerDriver).GetField("LastSync", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SyncSerializingField = typeof(VMServerDriver).GetField("SyncSerializing", BindingFlags.NonPublic | BindingFlags.Instance);

        private const int FRESH_SYNC_COOLDOWN = 10 * 30; //ticks between host reloads forced by guests losing sync
        private int TicksSinceFreshSync = FRESH_SYNC_COOLDOWN;

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
            FixSimulationInfo(vm);
            if (TicksSinceFreshSync < FRESH_SYNC_COOLDOWN) TicksSinceFreshSync++;
            PendingSpeeds.Add((short)System.Math.Max(short.MinValue, System.Math.Min(short.MaxValue, vm.SpeedMultiplier)));
            var result = base.Tick(vm);
            //again, now: a lot is loaded by a command inside the tick, and the state for guests is taken at the
            //end of that same tick. It is written out in the background with this part last, well after we return.
            FixSimulationInfo(vm);
            PendingClaims.Clear(); //everything queued before this tick has now run

            var history = SyncHistoryField?.GetValue(this);
            if (history != null && !ReferenceEquals(history, LastSyncHistory))
            {
                //a lot state was just taken. Guests replay every tick since it, so the neighbourhood
                //they get must be from this same moment.
                LastSyncHistory = history;
                CaptureWorld(vm);
            }
            else if (SyncHistoryField == null && SyncWorld == null && GuestIDs.Count > 0)
            {
                CaptureWorld(vm); //a different FreeSO: the best we can do is the neighbourhood as the first guest joins
            }
            return result;
        }

        /// <summary>
        /// FreeSO writes the lot's SIMI in the long layout when it came from a newer game file, but always labels
        /// and reads it as the short one - which makes the whole lot state unreadable for guests. This marks it
        /// short, as saving the lot does (VMTS1LotState.UpdateSIMI).
        /// </summary>
        private static void FixSimulationInfo(VM vm)
        {
            var simi = vm.TS1State?.SimulationInfo;
            if (simi != null && simi.Version > 0x3E) simi.Version = 0x3E;
        }

        private void RequestFreshSync()
        {
            if (TicksSinceFreshSync < FRESH_SYNC_COOLDOWN) return;
            TicksSinceFreshSync = 0;
            SelfResync = true;
            try
            {
                //drop the state kept for joiners, unless one is being written right now, so a new one is taken
                if (LastSyncField != null && SyncHistoryField != null && SyncSerializingField != null
                    && !(bool)SyncSerializingField.GetValue(this))
                {
                    LastSyncField.SetValue(this, null);
                    SyncHistoryField.SetValue(this, null);
                }
            }
            catch (System.Exception) { } //a different FreeSO: guests wait for the base driver instead
        }

        private void CaptureWorld(VM vm)
        {
            try
            {
                SyncWorld = CoopWorld.Capture(vm).Pack();
            }
            catch (System.Exception e)
            {
                SyncWorld = null;
                System.Console.WriteLine("COOP could not capture the neighbourhood for guests: " + e.Message);
            }
        }

        public override void SendCommand(VMNetCommandBodyAbstract cmd)
        {
            if (CoopProtocol.IsGuestID(cmd.ActorUID) && (cmd is VMNetSimJoinCmd || cmd is VMNetSimLeaveCmd))
            {
                //in TS1 mode a join grabs the first family member (often the host's Sim) and a leave walks
                //the Sim off the lot. Guests pick a free Sim themselves and just let go of it when they leave.
                return;
            }

            if (GuestIDs.Count > 0)
            {
                //A lot that was just built from a house file is not quite the same as that lot saved and loaded
                //again, and saved and loaded is what guests get. So with guests around, the host reloads its own
                //lot from a save as well (FreeSO's "self resync") in the tick the house is built - which is before
                //the state for guests is taken - so both sides start from exactly the same thing.
                if (cmd is VMBlueprintRestoreCmd) SelfResync = true;
                //and if a guest still loses sync, do the same again and send everyone a state taken right after,
                //rather than leave them out of step until the base driver next gets round to it (up to 30 seconds)
                else if (cmd is VMRequestResyncCmd && CoopProtocol.IsGuestID(cmd.ActorUID)) RequestFreshSync();
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
                //any member of the family on the lot, not just its active ones: see TS1GameScreen.IsPlayable
                if (family?.FamilyGUIDs == null || !family.FamilyGUIDs.Contains(target.Object.OBJ.GUID)) return false;
            }
            return true;
        }
    }
}
