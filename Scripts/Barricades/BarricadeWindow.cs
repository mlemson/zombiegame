using System.Collections.Generic;
using UnityEngine;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using Unity.AI.Navigation;
using Unity.FPS.AI;
using ZombieTown.Multiplayer;
namespace ZombieTown.Foundation
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BarricadeWindow : NetworkBehaviour
    {
        public string windowId;
        public BarricadeDefinition definition;
        public EconomyProfile economy;
        public DefenseSupplyProfile supplies;
        public bool requireCarriedPlanks;
        public bool vehicleMounted;
        [Min(0)] public int initialMissingBoards;
        public ZombieTown.LevelTwo.RadioOutpostMissionController radioDefense;
        public bool Available => radioDefense == null || radioDefense.CurrentPhase == ZombieTown.LevelTwo.RadioOutpostMissionController.MissionPhase.Defend || radioDefense.CurrentPhase == ZombieTown.LevelTwo.RadioOutpostMissionController.MissionPhase.Complete;
        static readonly List<BarricadeWindow> Windows=new();
        public static BarricadeWindow FindAimedWindow(Ray aim,float reach){
            BarricadeWindow best=null;
            foreach(var window in Windows){
                if(window==null || !window.Available || window.playerRepairPoint==null)continue;
                var plane=new Plane(window.transform.forward,window.transform.TransformPoint(window.openingCenter));
                if(!plane.Raycast(aim,out float distance) || distance>reach)continue;
                Vector3 local=window.transform.InverseTransformPoint(aim.GetPoint(distance));
                if(Mathf.Abs(local.x-window.openingCenter.x)>window.openingSize.x*.5f+.15f || Mathf.Abs(local.y-window.openingCenter.y)>window.openingSize.y*.5f+.15f)continue;
                if(Physics.Raycast(aim,out var hit,Mathf.Max(0,distance-.15f),~0,QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(window.transform))continue;
                best=window;reach=distance;
            }
            return best;
        }
        public static bool TryHandleZombie(ZombieAI zombie, Transform target) {
            if(zombie==null || target==null)return false;
            foreach(var w in Windows){
                if(w==null || w.vehicleMounted || !w.IsServer || !w.Available || w.zombieTraverseStart==null || Vector3.Distance(zombie.transform.position,w.transform.position)>9)continue;
                var a=w.transform.InverseTransformPoint(zombie.transform.position);var b=w.transform.InverseTransformPoint(target.position);
                if(a.z>=-.1f || b.z<=.1f || Mathf.Abs(a.y)>2.5f)continue;
                float x=Mathf.Lerp(a.x,b.x,-a.z/(b.z-a.z));if(Mathf.Abs(x)>1.4f)continue;
                var traversal=zombie.GetComponent<ZombieBreachTraversal>()??zombie.gameObject.AddComponent<ZombieBreachTraversal>();traversal.Assign(w);return traversal.Tick();
            }
            return false;
        }
        public bool TryInstallPlank(PlayerClassController player) {
            if(!IsServer || !Available || player==null || playerRepairPoint==null ||
                Vector3.Distance(player.transform.position,transform.position)>Radius+1 || health==null || (occupant!=null && IsBreached))return false;
            for(int i=0;i<Count;i++)if((ActiveBoardMask.Value&(1<<i))==0 || health[i]<BoardHealth){
                health[i]=BoardHealth;ActiveBoardMask.Value|=1<<i;BoardSoundRpc(false);player.AwardBarrierRepair(economy);return true;
            }
            return false;
        }
        [Min(0)] public float traversalLift=.08f;
        public Vector3 openingCenter=new(0,1.5f,0);
        public Vector2 openingSize=new(3,2.5f);
        PlayerInputHandler localRepairInput;
        bool locallyRepairing;
        bool Safe(Vector3 p)=>zombieTraverseStart!=null && zombieTraverseEnd!=null && Vector3.Dot(p-transform.position,zombieTraverseEnd.position-zombieTraverseStart.position)>0;
        public bool useDefinitionDefaults=true, overrideBoardSettings, overrideRepairSettings, overrideInteractionRadius;
        [Range(1,4)] public int boardCount=4;
        public float boardHealth=30, repairTimePerBoard=1.2f, repairCooldown=.3f, interactionRadius=3;
        public GameObject[] boards;
        public Transform zombieApproach, zombieTraverseStart, zombieTraverseEnd, playerRepairPoint;
        public NavMeshLink navigationLink;
        public readonly NetworkVariable<int> ActiveBoardMask=new();
        float[] health;
        Component occupant;
        sealed class Repair { public float lastTick, progress, nextBoard; }
        readonly Dictionary<ulong,Repair> repairing=new();
        readonly List<ulong> expired=new();
        float nextClientTick;
        int Count=>Mathf.Clamp(Mathf.Min(boards?.Length??0,useDefinitionDefaults && !overrideBoardSettings && definition!=null?definition.boardCount:boardCount),0,4);
        bool wasAvailable;
        float BoardHealth=>useDefinitionDefaults && !overrideBoardSettings && definition!=null?definition.boardHealth:boardHealth;
        float RepairTime=>useDefinitionDefaults && !overrideRepairSettings && definition!=null?definition.repairTimePerBoard:repairTimePerBoard;
        float Cooldown=>useDefinitionDefaults && !overrideRepairSettings && definition!=null?definition.repairCooldown:repairCooldown;
        public float Radius=>useDefinitionDefaults && !overrideInteractionRadius && definition!=null?definition.interactionRadius:interactionRadius;
        public bool IsBreached=>ActiveBoardMask.Value==0;
        public bool IsOccupied=>occupant!=null;
        public override void OnNetworkSpawn() {
            Windows.Add(this);
            health=new float[Count];
            if(IsServer) { for(int i=0;i<Count;i++) health[i]=BoardHealth; ActiveBoardMask.Value=(1<<Mathf.Max(0,Count-initialMissingBoards))-1; }
            ActiveBoardMask.OnValueChanged+=Changed; Apply();
        }
        public override void OnNetworkDespawn() { Windows.Remove(this);ActiveBoardMask.OnValueChanged-=Changed; repairing.Clear(); occupant=null; localRepairInput?.SetRepairFireBlocked(false);localRepairInput=null;locallyRepairing=false; }
        void Changed(int a,int b)=>Apply();
        [Rpc(SendTo.ClientsAndHost)]
        void BoardSoundRpc(bool broken) => DefenseAudioSettings.Play(broken?DefenseAudioSettings.Cue.Broken:DefenseAudioSettings.Cue.Placed,transform.TransformPoint(openingCenter));
        void Apply() {
            wasAvailable=Available;
            if(boards!=null) for(int i=0;i<boards.Length;i++) if(boards[i]!=null) boards[i].SetActive(Available && i<Count && (ActiveBoardMask.Value&(1<<i))!=0);
            if(navigationLink!=null) navigationLink.enabled=IsBreached;
        }
        public bool Claim(ZombieBreachTraversal zombie) { if(!IsServer || !Available || (occupant!=null && occupant!=zombie)) return false; occupant=zombie; return true; }
        public void Release(ZombieBreachTraversal zombie) { if(occupant==zombie) occupant=null; }
        public bool ClaimVehicle(ZombieAI zombie) { if(!IsServer || !Available || (occupant!=null && occupant!=zombie))return false;occupant=zombie;return true; }
        public void ReleaseVehicle(ZombieAI zombie) { if(occupant==zombie)occupant=null; }
        public void AttackVehicleBoard(ZombieAI zombie) { if(occupant==zombie)DamageBoard(zombie); }
        public void AttackBoard(ZombieBreachTraversal zombie,ZombieAI attacker) {
            if(occupant==zombie)DamageBoard(attacker);
        }
        void DamageBoard(ZombieAI attacker) {
            if(!IsServer || attacker==null || health==null) return;
            for(int i=0;i<Count;i++) if((ActiveBoardMask.Value&(1<<i))!=0) { health[i]-=Mathf.Max(0,attacker.AttackDamage); if(health[i]<=0) { ActiveBoardMask.Value &= ~(1<<i); BoardSoundRpc(true); } return; }
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone,Delivery=RpcDelivery.Unreliable)]
        public void RepairTickRpc(RpcParams rpc=default) {
            if(requireCarriedPlanks)return;
            if(playerRepairPoint==null || !FoundationInteraction.TryPlayer(rpc.Receive.SenderClientId,playerRepairPoint.position,Radius,out var player) || player.IsUsingMountedGun || player.IsRepairingGate || !Safe(player.transform.position) || ActiveBoardMask.Value==(1<<Count)-1) return;
            if(!repairing.TryGetValue(rpc.Receive.SenderClientId,out var state)) { state=new Repair(); repairing.Add(rpc.Receive.SenderClientId,state); }
            state.lastTick=Time.time;player.WindowRepairLeaseUntil=Time.time+.25f;
        }
        void Update() {
            if(!IsSpawned || playerRepairPoint==null) return;
            if(radioDefense!=null && wasAvailable!=Available)Apply();
            if(!Available)return;
            var local=FoundationInteraction.Local(playerRepairPoint.position,Radius);
            var input=local!=null?local.GetComponent<PlayerInputHandler>():null;
            bool held=!requireCarriedPlanks && local!=null && !local.IsUsingMountedGun && !local.IsRepairingGate && input!=null && input.CanProcessInput() && Safe(local.transform.position) && !IsFullyRepaired && FoundationInteraction.Held;
            if(held){localRepairInput=input;locallyRepairing=true;input.SetRepairFireBlocked(true);if(Time.time>=nextClientTick){nextClientTick=Time.time+.1f;RepairTickRpc();}}
            else if(locallyRepairing){localRepairInput?.SetRepairFireBlocked(false);localRepairInput=null;locallyRepairing=false;EndRepairRpc();}
            if(!IsServer) return;
            expired.Clear();
            foreach(var entry in repairing) {
                var state=entry.Value;
                if(Time.time-state.lastTick>.25f || !FoundationInteraction.TryPlayer(entry.Key,playerRepairPoint.position,Radius,out var player) || player.IsUsingMountedGun || !Safe(player.transform.position)) { expired.Add(entry.Key); continue; }
                if((occupant!=null && IsBreached) || Time.time<state.nextBoard) continue;
                state.progress+=Time.deltaTime;
                if(state.progress<Mathf.Max(.1f,RepairTime)) continue;
                state.progress=0; state.nextBoard=Time.time+Cooldown;
                for(int i=0;i<Count;i++) if((ActiveBoardMask.Value&(1<<i))==0) {
                    if(supplies!=null && !player.TryPayRepair(BoardHealth,supplies))break;
                    health[i]=BoardHealth; ActiveBoardMask.Value|=1<<i; BoardSoundRpc(false);
                    player.AwardBarrierRepair(economy); break;
                }
            }
            foreach(var id in expired) repairing.Remove(id);
        }
        bool IsFullyRepaired=>ActiveBoardMask.Value==(1<<Count)-1;
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        void EndRepairRpc(RpcParams rpc=default) {repairing.Remove(rpc.Receive.SenderClientId);if(FoundationInteraction.TryPlayer(rpc.Receive.SenderClientId,playerRepairPoint.position,Radius,out var p))p.WindowRepairLeaseUntil=0;}
        void OnGUI() { if(Available && playerRepairPoint!=null && FoundationInteraction.Local(playerRepairPoint.position,Radius) is var p && p!=null && !p.IsUsingMountedGun && Safe(p.transform.position)) FoundationInteraction.Prompt(requireCarriedPlanks?Unity.FPS.Game.GameLocalization.Text("Bring a plank; F to board up this window","Breng een plank; F om dit raam te barricaderen"):Unity.FPS.Game.GameLocalization.Text("HOLD F TO REPAIR BARRIER","HOUD F OM BARRICADE TE REPAREREN")); }
        void OnDrawGizmosSelected() { Gizmos.color=Color.green; if(zombieTraverseStart!=null && zombieTraverseEnd!=null) Gizmos.DrawLine(zombieTraverseStart.position,zombieTraverseEnd.position); if(playerRepairPoint!=null) Gizmos.DrawWireSphere(playerRepairPoint.position,Radius); }
    }
}
