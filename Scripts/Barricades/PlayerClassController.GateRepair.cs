using ZombieTown.Foundation;
using UnityEngine;
namespace ZombieTown.Multiplayer
{
    public sealed partial class PlayerClassController
    {
        RepairableGate repairingGate;
        public bool IsRepairingGate => repairingGate != null && repairingGate.HasRepairLease(this);
        public bool BeginGateRepair(RepairableGate gate)
        {
            if (!IsServer || IsUsingMountedGun || Time.time < WindowRepairLeaseUntil || gate == null || (repairingGate != null && repairingGate != gate && IsRepairingGate)) return false;
            repairingGate = gate;
            return true;
        }
        public void EndGateRepair(RepairableGate gate) { if (repairingGate == gate) repairingGate = null; }
    }
}
