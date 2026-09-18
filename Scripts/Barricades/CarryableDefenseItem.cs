using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.FPS.AI;
using ZombieTown.Multiplayer;

namespace ZombieTown.Foundation
{
    [DefaultExecutionOrder(-1000),RequireComponent(typeof(NetworkObject),typeof(BoxCollider))]
    public sealed class CarryableDefenseItem:NetworkBehaviour
    {
        public bool isPlank;
        [Tooltip("When assigned, placing this kit deploys a fixed turret and consumes the kit.")]
        public GameObject deployableTurretPrefab;
        public float maximumHealth=180;
        public Vector3 heldPosition=new(.25f,-.3f,.8f);
        public Vector3 heldEuler=new(0,0,15);
        public readonly NetworkVariable<ulong> Carrier=new(ulong.MaxValue);
        public readonly NetworkVariable<bool> Used=new();
        public readonly NetworkVariable<Vector3> WorldPosition=new();
        public readonly NetworkVariable<Quaternion> WorldRotation=new(Quaternion.identity);
        public readonly NetworkVariable<NetworkObjectReference> RestingVehicle=new();
        public readonly NetworkVariable<Vector3> VehicleLocalPosition=new();
        public readonly NetworkVariable<Quaternion> VehicleLocalRotation=new(Quaternion.identity);
        NetworkObject dropVehicle;
        int originalLayer;
        public void RestOnVehicle(NetworkObject vehicle,Vector3 position,Quaternion rotation){
            if(!IsServer)return;
            WorldPosition.Value=position;WorldRotation.Value=rotation;
            RestingVehicle.Value=vehicle!=null?new NetworkObjectReference(vehicle):default;
            if(vehicle!=null){VehicleLocalPosition.Value=vehicle.transform.InverseTransformPoint(position);VehicleLocalRotation.Value=Quaternion.Inverse(vehicle.transform.rotation)*rotation;}
        }
        public readonly NetworkVariable<float> Durability=new();
        static readonly List<CarryableDefenseItem> Items=new();
        public static CarryableDefenseItem LocalCarried {get;private set;}
        public static bool IsCarrying(ulong clientId)=>Items.Exists(item=>item!=null && item.IsSpawned && item.Carrier.Value==clientId);
        readonly Collider[] nearby=new Collider[48];
        readonly Dictionary<ZombieAI,float> nextAttack=new();
        readonly List<ZombieAI> stale=new();
        Renderer[] visuals;
        BoxCollider shape;
        NavMeshObstacle obstacle;
        WeaponController hiddenWeapon;
        PlayerWeaponsManager localWeapons;
        bool localCarrying, weaponWasShown;
        float nextDamageCheck;
        public static bool TryHandleZombie(ZombieAI zombie,Transform target,NavMeshAgent agent){
            if(zombie==null || target==null || agent==null || !agent.enabled || !agent.isOnNavMesh)return false;
            Vector3 origin=zombie.transform.position+Vector3.up*.7f;
            Vector3 delta=target.position+Vector3.up*.7f-origin;
            if(delta.sqrMagnitude<.01f)return false;
            CarryableDefenseItem nearest=null;float distance=Mathf.Min(5f,delta.magnitude);
            foreach(var item in Items){
                if(item==null || !item.IsServer || item.isPlank || item.Used.Value || item.Carrier.Value!=ulong.MaxValue || !item.shape.enabled)continue;
                if(item.shape.Raycast(new Ray(origin,delta.normalized),out var hit,distance)){nearest=item;distance=hit.distance;}
            }
            if(nearest==null)return false;
            if(Physics.Linecast(origin,nearest.shape.ClosestPoint(origin),out var obstruction,~0,QueryTriggerInteraction.Ignore) &&
                !obstruction.transform.IsChildOf(zombie.transform) && obstruction.collider.GetComponentInParent<CarryableDefenseItem>()!=nearest)return false;
            Vector3 edge=nearest.shape.ClosestPoint(origin);float gap=Vector3.Distance(origin,edge);
            agent.isStopped=gap<.85f;
            if(agent.isStopped)agent.velocity=Vector3.zero;
            else {Vector3 approach=edge+(origin-edge).normalized*.65f; if(NavMesh.SamplePosition(approach,out var nav,1.5f,NavMesh.AllAreas))agent.SetDestination(nav.position);}
            if(!agent.updatePosition && !agent.isStopped){Vector3 next=agent.nextPosition+Vector3.up*zombie.GroundOffset; if(!nearest.shape.bounds.Contains(next+Vector3.up*.7f))zombie.transform.position=next;}
            Vector3 facing=nearest.transform.position-zombie.transform.position;facing.y=0;
            if(facing.sqrMagnitude>.01f)zombie.transform.rotation=Quaternion.RotateTowards(zombie.transform.rotation,Quaternion.LookRotation(facing),240*Time.deltaTime);
            return true;
        }
        void Awake(){originalLayer=gameObject.layer;visuals=GetComponentsInChildren<Renderer>(true);shape=GetComponent<BoxCollider>();obstacle=GetComponent<NavMeshObstacle>();}
        public override void OnNetworkSpawn(){
            Items.Add(this);
            if(IsServer){WorldPosition.Value=transform.position;WorldRotation.Value=transform.rotation;Durability.Value=maximumHealth;}
        }
        public override void OnNetworkDespawn(){Items.Remove(this);RestoreWeapon();}
        void OnDisable(){Items.Remove(this);RestoreWeapon();}
        bool TryCarrier(ulong id,out PlayerClassController player){
            player=null;
            if(NetworkManager==null || !NetworkManager.ConnectedClients.TryGetValue(id,out var client) || client.PlayerObject==null)return false;
            player=client.PlayerObject.GetComponent<PlayerClassController>();
            return player!=null && player.IsReady.Value && player.RoundStarted.Value && !player.IsDowned.Value;
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void TakeRpc(RpcParams rpc=default){
            ulong id=rpc.Receive.SenderClientId;
            if(Used.Value || Carrier.Value!=ulong.MaxValue || !TryCarrier(id,out var player) || player.IsUsingMountedGun ||
                player.IsRepairingDefense || Vector3.Distance(player.transform.position,transform.position)>3.5f)return;
            foreach(var item in Items)if(item!=null && item.Carrier.Value==id)return;
            if(Physics.Linecast(player.transform.position+Vector3.up*1.9f,transform.position,out var blocker,~0,QueryTriggerInteraction.Ignore) && blocker.collider.GetComponentInParent<CarryableDefenseItem>()!=this)return;
            Carrier.Value=id;
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void PlaceRpc(NetworkObjectReference target,bool attach,Vector3 proposed,RpcParams rpc=default){
            if(Carrier.Value!=rpc.Receive.SenderClientId || Used.Value || !TryCarrier(Carrier.Value,out var player))return;
            if(attach){
                if(!isPlank || !target.TryGet(out var obj) || Vector3.Distance(player.transform.position,obj.transform.position)>5)return;
                bool applied=obj.TryGetComponent<BarricadeWindow>(out var window)?window.TryInstallPlank(player):
                    obj.TryGetComponent<RepairableGate>(out var gate) && gate.TryInstallPlank(player);
                if(applied){Used.Value=true;Carrier.Value=ulong.MaxValue;}
                return;
            }
            if(!float.IsFinite(proposed.x)||!float.IsFinite(proposed.y)||!float.IsFinite(proposed.z)||Vector3.Distance(proposed,player.transform.position)>3.5f)return;
            if(TryDropPosition(proposed,player,out var position)){
                if(deployableTurretPrefab!=null){
                    // A placed turret stays on the ground; vehicles cannot carry a deployed one.
                    if(dropVehicle!=null)return;
                    Vector3 groundPosition=position-Vector3.up*(Mathf.Abs(shape.size.y*transform.lossyScale.y)*.5f+.035f);
                    var turret=Instantiate(deployableTurretPrefab,groundPosition,Quaternion.Euler(0,player.transform.eulerAngles.y,0)).GetComponent<ZombieTown.Finale.PortableAutoTurret>();
                    turret.DeployedBy.Value=player.OwnerClientId;turret.NetworkObject.Spawn();
                    Used.Value=true;Carrier.Value=ulong.MaxValue;NetworkObject.Despawn(true);return;
                }
                RestOnVehicle(dropVehicle,position,Quaternion.Euler(0,player.transform.eulerAngles.y,0));Carrier.Value=ulong.MaxValue;
            }
        }
        bool TryDropPosition(Vector3 proposed,PlayerClassController player,out Vector3 result){
            dropVehicle=null;
            result=proposed;RaycastHit? ground=null;
            foreach(var h in Physics.RaycastAll(proposed+Vector3.up*1.5f,Vector3.down,4,~0,QueryTriggerInteraction.Ignore)){
                if(h.transform.IsChildOf(transform)||h.transform.IsChildOf(player.transform)||h.normal.y<.65f)continue;
                if(!ground.HasValue || h.distance<ground.Value.distance)ground=h;
            }
            if(!ground.HasValue)return false;
            dropVehicle=ground.Value.collider.GetComponentInParent<BusDefenseLayout>()?.NetworkObject;
            Vector3 half=Vector3.Scale(shape.size,transform.lossyScale)*.5f;
            result=ground.Value.point+Vector3.up*(half.y+.035f);
            int count=Physics.OverlapBoxNonAlloc(result,half*.92f,nearby,Quaternion.Euler(0,player.transform.eulerAngles.y,0),~0,QueryTriggerInteraction.Ignore);
            if(count==nearby.Length)return false;
            for(int i=0;i<count;i++)if(!nearby[i].transform.IsChildOf(transform))return false;
            return true;
        }
        void Update(){
            if(!IsSpawned)return;
            bool held=Carrier.Value!=ulong.MaxValue;
            int attachmentLayer=LayerMask.NameToLayer("BusAttachment");
            bool aboard=RestingVehicle.Value.TryGet(out _);
            gameObject.layer=aboard && attachmentLayer>=0?attachmentLayer:originalLayer;
            bool ours=IsClient && held && Carrier.Value==NetworkManager.LocalClientId;
            if(localCarrying && !ours)RestoreWeapon();
            shape.enabled=!held && !Used.Value;
            if(obstacle!=null)obstacle.enabled=!held && !Used.Value;
            foreach(var r in visuals)if(r!=null)r.enabled=!Used.Value;
            if(Used.Value)return;
            if(!held){
                if(RestingVehicle.Value.TryGet(out var vehicle))transform.SetPositionAndRotation(vehicle.transform.TransformPoint(VehicleLocalPosition.Value),vehicle.transform.rotation*VehicleLocalRotation.Value);
                else transform.SetPositionAndRotation(WorldPosition.Value,WorldRotation.Value);
                TryTakeLocal();
            }
            else if(ours)UpdateLocalCarry();
            else if(NetworkManager.SpawnManager.GetPlayerNetworkObject(Carrier.Value) is var avatar && avatar!=null)
                transform.SetPositionAndRotation(avatar.transform.TransformPoint(new Vector3(.3f,1.2f,.8f)),avatar.transform.rotation*Quaternion.Euler(heldEuler));
            if(IsServer && held && !TryCarrier(Carrier.Value,out _)){
                // Return to the last validated resting position on death/disconnect.
                // The held camera position can be inside a wall or above empty space.
                Carrier.Value=ulong.MaxValue;
            }
            if(IsServer && !held && !isPlank && Time.time>=nextDamageCheck){nextDamageCheck=Time.time+.2f;DamageFromZombies();}
        }
        void TryTakeLocal(){
            if(!GameplayInteraction.Pressed)return;
            var player=FoundationInteraction.Local(transform.position,3.5f);
            if(player==null || player.IsUsingMountedGun)return;
            var input=player.GetComponent<PlayerInputHandler>();var view=player.GetComponent<PlayerCharacterController>()?.PlayerCamera;
            if(input==null || !input.CanProcessInput() || view==null)return;
            if(Physics.SphereCast(view.transform.position,.3f,view.transform.forward,out var hit,3.5f,~0,QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<CarryableDefenseItem>()==this){GameplayInteraction.Consume();TakeRpc();}
        }
        void UpdateLocalCarry(){
            var player=NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerClassController>();if(player==null)return;
            var movement=player.GetComponent<PlayerCharacterController>();var view=movement!=null?movement.PlayerCamera:null;if(view==null)return;
            if(!localCarrying){
                localCarrying=true;localWeapons=player.GetComponent<PlayerWeaponsManager>();hiddenWeapon=localWeapons?.GetActiveWeapon();
                weaponWasShown=hiddenWeapon!=null && hiddenWeapon.IsWeaponActive;
                hiddenWeapon?.CancelPendingShot();hiddenWeapon?.ShowWeapon(false);
            }
            GameplayInteraction.Carrying=true;
            LocalCarried=this;
            transform.SetPositionAndRotation(view.transform.TransformPoint(heldPosition),view.transform.rotation*Quaternion.Euler(heldEuler));
            var input=player.GetComponent<PlayerInputHandler>();if(input==null || !input.CanProcessInput() || !GameplayInteraction.RawPressed)return;
            GameplayInteraction.Consume();
            if(isPlank){var aimed=BarricadeWindow.FindAimedWindow(new Ray(view.transform.position,view.transform.forward),3.5f);if(aimed!=null){PlaceRpc(new NetworkObjectReference(aimed.NetworkObject),true,Vector3.zero);return;}}
            if(isPlank && Physics.Raycast(view.transform.position,view.transform.forward,out var hit,3.5f,~0,QueryTriggerInteraction.Collide)){
                var window=hit.collider.GetComponentInParent<BarricadeWindow>();var gate=hit.collider.GetComponentInParent<RepairableGate>();
                NetworkObject target=window!=null?window.NetworkObject:gate!=null?gate.NetworkObject:null;
                if(target!=null){PlaceRpc(new NetworkObjectReference(target),true,Vector3.zero);return;}
            }
            Vector3 proposed=player.transform.position+Vector3.ProjectOnPlane(view.transform.forward,Vector3.up).normalized*1.8f;
            PlaceRpc(default,false,proposed);
        }
        void RestoreWeapon(){
            if(!localCarrying)return;
            GameplayInteraction.Carrying=false;
            if(LocalCarried==this)LocalCarried=null;
            if(weaponWasShown && hiddenWeapon!=null && localWeapons!=null && localWeapons.GetActiveWeapon()==hiddenWeapon)hiddenWeapon.ShowWeapon(true);
            localCarrying=false;hiddenWeapon=null;localWeapons=null;
        }
        void LateUpdate(){
            if(!localCarrying || !IsSpawned)return;
            var view=localWeapons!=null?localWeapons.GetComponent<PlayerCharacterController>()?.PlayerCamera:null;
            if(view!=null)transform.SetPositionAndRotation(view.transform.TransformPoint(heldPosition),view.transform.rotation*Quaternion.Euler(heldEuler));
        }
        void DamageFromZombies(){
            int count=Physics.OverlapSphereNonAlloc(transform.position,1.9f,nearby,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++){
                var zombie=nearby[i].GetComponentInParent<ZombieAI>();
                if(zombie==null || zombie.GetComponent<Health>() is not Health hp || hp.CurrentHealth<=0 ||
                    (nextAttack.TryGetValue(zombie,out var next)&&Time.time<next))continue;
                if(Physics.Linecast(zombie.transform.position+Vector3.up,transform.position,out var blocked,~0,QueryTriggerInteraction.Ignore) && blocked.collider.GetComponentInParent<CarryableDefenseItem>()!=this)continue;
                nextAttack[zombie]=Time.time+Mathf.Max(.5f,zombie.AttackInterval);
                zombie.GetComponent<NetworkZombieAnimator>()?.PlayAttack();
                Durability.Value=Mathf.Max(0,Durability.Value-Mathf.Max(0,zombie.AttackDamage));
                if(Durability.Value<=0){Used.Value=true;return;}
            }
            stale.Clear();foreach(var pair in nextAttack)if(pair.Key==null || Time.time>pair.Value+10)stale.Add(pair.Key);
            foreach(var zombie in stale)nextAttack.Remove(zombie);
        }
        void OnGUI(){
            if(!IsSpawned || Used.Value)return;
            if(localCarrying){FoundationInteraction.Prompt(deployableTurretPrefab!=null?GameLocalization.Text("F: deploy turret on ground - fixed for 60 seconds","F: turret op de grond plaatsen - staat 60 seconden vast"):isPlank?GameLocalization.Text("F: attach plank to window/gate, or put it down","F: plank vastmaken aan raam/poort, of neerleggen"):GameLocalization.Text("F: put down barricade crate","F: barricadekrat neerzetten"));return;}
            var player=FoundationInteraction.Local(transform.position,3.5f);
            var camera=player!=null?player.GetComponent<PlayerCharacterController>()?.PlayerCamera:null;
            if(Carrier.Value==ulong.MaxValue && camera!=null && Physics.SphereCast(camera.transform.position,.3f,camera.transform.forward,out var hit,3.5f,~0,QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<CarryableDefenseItem>()==this)
                FoundationInteraction.Prompt(GameLocalization.Text("F: pick up ","F: oppakken ")+(deployableTurretPrefab!=null?"turret":isPlank?GameLocalization.Text("plank","plank"):GameLocalization.Text("barricade crate","barricadekrat")));
        }
    }
}
