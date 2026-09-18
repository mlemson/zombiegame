using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ZombieTown.Multiplayer;
namespace ZombieTown.Foundation
{
    public sealed class ZombieSpawnPoint : MonoBehaviour
    {
        public static readonly List<ZombieSpawnPoint> Active=new();
        public string spawnId, spawnGroup;
        public ProgressionZone zone;
        [Min(1)] public int minimumStage=1, maximumStage=1;
        [Min(0)] public float weight=1, spawnRadius=.5f, minimumPlayerDistance=5, maximumPlayerDistance=150;
        public bool spawnEnabled=true, requireOutsidePlayerLineOfSight;
        public EnemyRole allowedRoles=EnemyRole.All;
        public ProgressionGate requiresGate;
        public BarricadeWindow breachWindow;
        void OnEnable() { if(!Active.Contains(this)) Active.Add(this); }
        void OnDisable() => Active.Remove(this);
        public bool Eligible(int stage, EnemyRole role, LevelEncounterProfile.Stage data)
        {
            if(!isActiveAndEnabled || !spawnEnabled || weight<=0 || stage<minimumStage || (maximumStage>0 && stage>maximumStage) || (allowedRoles&role)==0 || (requiresGate!=null && !requiresGate.IsOpen)) return false;
            if(data!=null && System.Array.IndexOf(data.allowedSpawnGroups,spawnGroup)<0) return false;
            return true;
        }
        public bool TryPosition(out Vector3 position)
        {
            var offset=Random.insideUnitCircle*spawnRadius;
            position=transform.position+new Vector3(offset.x,0,offset.y);
            if(!NavMesh.SamplePosition(position,out var hit,1,NavMesh.AllAreas)) return false;
            position=hit.position;
            var manager=Unity.Netcode.NetworkManager.Singleton;
            if(manager==null || !manager.IsListening) return true;
            float closest=float.PositiveInfinity;
            foreach(var obj in manager.SpawnManager.SpawnedObjectsList) {
                var player=obj.GetComponent<PlayerClassController>();
                if(player==null || !player.IsReady.Value || player.IsDowned.Value) continue;
                float distance=Vector3.Distance(position,player.transform.position); closest=Mathf.Min(closest,distance);
                if(distance<minimumPlayerDistance) return false;
                if(requireOutsidePlayerLineOfSight && !Physics.Linecast(position+Vector3.up,player.transform.position+Vector3.up,out _,~0,QueryTriggerInteraction.Ignore)) return false;
            }
            return float.IsPositiveInfinity(closest) || maximumPlayerDistance<=0 || closest<=maximumPlayerDistance;
        }
        void OnDrawGizmos() { Gizmos.color=Color.HSVToRGB((minimumStage-1)*.23f,.8f,1); Gizmos.DrawWireSphere(transform.position,Mathf.Max(.25f,spawnRadius)); Gizmos.DrawRay(transform.position,transform.forward*2);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position+Vector3.up,spawnId+"\n"+spawnGroup+" stage "+minimumStage);
#endif
        }
    }
}
