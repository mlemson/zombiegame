using UnityEngine;
using Unity.Netcode;
using Unity.FPS.Gameplay;
using ZombieTown.Multiplayer;
namespace ZombieTown.Foundation
{
    [RequireComponent(typeof(RepairableGate))]
    public sealed class BarricadeBuildSite : NetworkBehaviour
    {
        public DefenseSupplyProfile supplies;
        public GameObject placementMarker;
        RepairableGate gate;
        PlayerClassController builder;
        PlayerInputHandler localInput;
        float lastTick,progress,nextTick;
        bool localHeld;
        void Awake()=>gate=GetComponent<RepairableGate>();
        public override void OnNetworkDespawn()=>Release();
        void OnDisable()=>Release();
        void Release() { if(localInput!=null)localInput.SetRepairFireBlocked(false);localInput=null;localHeld=false;if(builder!=null)builder.WindowRepairLeaseUntil=0;builder=null;progress=0; }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void BuildRpc(bool held,RpcParams rpc=default) {
            if(!held) {if(builder!=null && builder.OwnerClientId==rpc.Receive.SenderClientId){builder.WindowRepairLeaseUntil=0;builder=null;progress=0;}return;}
            if(gate==null || gate.IsConstructed || supplies==null || gate.repairPoint==null ||
                !FoundationInteraction.TryPlayer(rpc.Receive.SenderClientId,gate.repairPoint.position,gate.interactionRadius,out var p) ||
                p.IsUsingMountedGun || p.IsRepairingGate || !gate.IsSafeSide(p.transform.position) || gate.OpeningOccupied() || (builder!=null && builder!=p))return;
            p.InitializeDefenseSupplies(supplies);if(p.Materials.Value<supplies.materialsPerBuild)return;
            builder=p;lastTick=Time.time;p.WindowRepairLeaseUntil=Time.time+.3f;
        }
        void Update() {
            if(!IsSpawned || gate==null || gate.repairPoint==null)return;
            if(placementMarker!=null)placementMarker.SetActive(!gate.IsConstructed);
            var p=FoundationInteraction.Local(gate.repairPoint.position,gate.interactionRadius);
            var input=p!=null?p.GetComponent<PlayerInputHandler>():null;
            bool held=!gate.IsConstructed && p!=null && !p.IsUsingMountedGun && input!=null && input.CanProcessInput() && gate.IsSafeSide(p.transform.position) && FoundationInteraction.Held;
            if(held) {localInput=input;localHeld=true;input.SetRepairFireBlocked(true);if(Time.time>=nextTick){nextTick=Time.time+.1f;BuildRpc(true);}}
            else if(localHeld) {localInput?.SetRepairFireBlocked(false);localInput=null;localHeld=false;BuildRpc(false);}
            if(!IsServer || builder==null)return;
            if(Time.time-lastTick>.3f || !FoundationInteraction.TryPlayer(builder.OwnerClientId,gate.repairPoint.position,gate.interactionRadius,out _) || builder.IsUsingMountedGun || gate.OpeningOccupied() || gate.IsConstructed) {
                builder.WindowRepairLeaseUntil=0;builder=null;progress=0;return;
            }
            progress+=Time.deltaTime;
            if(progress<Mathf.Max(.1f,supplies.buildSeconds))return;
            if(builder.Materials.Value>=supplies.materialsPerBuild && gate.Construct())builder.TrySpendMaterials(supplies.materialsPerBuild);
            builder.WindowRepairLeaseUntil=0;builder=null;progress=0;
        }
        void OnGUI() {
            if(gate==null || gate.IsConstructed || supplies==null || gate.repairPoint==null)return;
            var p=FoundationInteraction.Local(gate.repairPoint.position,gate.interactionRadius);
            if(p!=null && !p.IsUsingMountedGun && gate.IsSafeSide(p.transform.position))FoundationInteraction.Prompt(gate.OpeningOccupied()?"BOUWPLEK GEBLOKKEERD":$"HOUD F: BARRICADE BOUWEN ({supplies.materialsPerBuild} materialen / {supplies.buildSeconds:0.#}s)");
        }
    }
}
