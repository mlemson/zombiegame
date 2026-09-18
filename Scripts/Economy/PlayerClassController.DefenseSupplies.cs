using Unity.Netcode;
using UnityEngine;
using ZombieTown.Foundation;
namespace ZombieTown.Multiplayer
{
    public sealed partial class PlayerClassController
    {
        public readonly NetworkVariable<int> Materials=new();
        public readonly NetworkVariable<float> RepairMaterialCredit=new();
        bool initializedDefenseSupplies;
        public MountedMachineGun MountedGun {get;set;}
        public bool IsUsingMountedGun=>MountedGun!=null;
        public float WindowRepairLeaseUntil {get;set;}
        public bool IsRepairingDefense=>IsRepairingGate || Time.time<WindowRepairLeaseUntil;
        public void InitializeDefenseSupplies(DefenseSupplyProfile profile) {
            if(!IsServer || initializedDefenseSupplies || profile==null)return;
            initializedDefenseSupplies=true;Materials.Value=profile.startingMaterials;
        }
        public bool TrySpendMaterials(int cost) {
            if(!IsServer || cost<0 || Materials.Value<cost)return false;
            Materials.Value-=cost;return true;
        }
        public bool TryPayRepair(float health,DefenseSupplyProfile profile) {
            if(!IsServer || profile==null || health<=0 || IsUsingMountedGun)return false;
            InitializeDefenseSupplies(profile);
            float needed=Mathf.Max(0,health-RepairMaterialCredit.Value);
            int cost=Mathf.CeilToInt(needed/Mathf.Max(1,profile.repairHealthPerMaterial));
            if(!TrySpendMaterials(cost))return false;
            RepairMaterialCredit.Value=Mathf.Max(0,RepairMaterialCredit.Value+cost*profile.repairHealthPerMaterial-health);
            return true;
        }
    }
}
