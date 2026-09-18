using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using ZombieTown.Multiplayer;
namespace ZombieTown.Foundation
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ProgressionGate : NetworkBehaviour
    {
        public string gateId;
        public GateDefinition definition;
        public GameBalanceDatabase balance;
        public bool useDefinitionDefaults=true, overrideCost, overrideUnlockCondition, overrideInteractionRadius;
        public int localCostOverride=120, requiredStage=1, nextStage=2;
        public float interactionRadius=4;
        public Transform visualRoot;
        public Collider[] physicalColliders;
        public NavMeshObstacle[] obstacles;
        public readonly NetworkVariable<bool> Open=new();
        Vector3 closedPosition;
        bool cached;
        public bool IsOpen=>Open.Value;
        public int RequiredStage=>useDefinitionDefaults && !overrideUnlockCondition && definition!=null ? definition.requiredStage : requiredStage;
        public int NextStage=>useDefinitionDefaults && !overrideUnlockCondition && definition!=null ? definition.nextStage : nextStage;
        public float Radius=>useDefinitionDefaults && !overrideInteractionRadius && definition!=null ? definition.interactionRadius : interactionRadius;
        public int BaseCost=>useDefinitionDefaults && !overrideCost && definition!=null ? definition.baseCost : localCostOverride;
        public int ResolveCost(int players) => balance != null && balance.economy != null && balance.playerScaling != null
            ? balance.economy.GateCost(BaseCost,balance.playerScaling.Get(players).gateCostMultiplier)
            : BaseCost;
        public void ApplyMissionState(bool open) { if(IsServer && IsSpawned) Open.Value=open; Apply(open); }
        public override void OnNetworkSpawn() { Cache(); Open.OnValueChanged+=Changed; Apply(Open.Value); }
        public override void OnNetworkDespawn() { Open.OnValueChanged-=Changed; }
        void Changed(bool previous,bool current)=>Apply(current);
        void Cache() { if(cached) return; cached=true; if(visualRoot!=null) closedPosition=visualRoot.localPosition; }
        void Apply(bool open) { Cache(); if(physicalColliders!=null) foreach(var c in physicalColliders) if(c!=null) c.enabled=!open; if(obstacles!=null) foreach(var o in obstacles) if(o!=null) o.enabled=!open; }
        void Update() { if(!Application.isPlaying) return; Cache(); if(visualRoot!=null) visualRoot.localPosition=Vector3.MoveTowards(visualRoot.localPosition,closedPosition+(Open.Value?Vector3.up*(definition!=null?definition.openHeight:6.5f):Vector3.zero),Time.deltaTime*4.5f); }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void PurchaseRpc(RpcParams rpc=default) {
            if(Open.Value || !FoundationInteraction.TryPlayer(rpc.Receive.SenderClientId,transform.position,Radius,out var player)) return;
            var context=GameplaySceneContext.Active;
            if(context!=null && context.mission!=null) { context.mission.TryPurchaseAuthoredGate(this,player); return; }
            if(RequiredStage>1) return;
            int cost=ResolveCost(context!=null?context.PlayerCount:1);
            if(player.Points.Value<cost) return;
            player.Points.Value-=cost; Open.Value=true;
        }
        void OnDrawGizmosSelected() {
            Gizmos.color=Color.yellow; Gizmos.DrawWireSphere(transform.position,Radius);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position+Vector3.up*2,$"{gateId}\nBase {BaseCost}, solo {ResolveCost(1)}, next {NextStage}");
#endif
        }
    }
}
