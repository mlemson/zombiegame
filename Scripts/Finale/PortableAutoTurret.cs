using UnityEngine;
using Unity.Netcode;
using Unity.FPS.AI;
using Unity.FPS.Game;
using ZombieTown.Multiplayer;

namespace ZombieTown.Finale
{
    public sealed class PortableAutoTurret : NetworkBehaviour
    {
        public float lifetimeSeconds=60, range=26, damage=22, shotsPerSecond=4;
        public Transform muzzle, swivel;
        public AudioSource shotAudio;
        public LineRenderer tracer;
        public TextMesh timerLabel;
        public readonly NetworkVariable<double> ExpiresAt=new();
        public readonly NetworkVariable<ulong> DeployedBy=new();
        float nextShot, flashUntil;
        public override void OnNetworkSpawn(){if(IsServer)ExpiresAt.Value=NetworkManager.ServerTime.Time+lifetimeSeconds;}
        void Update()
        {
            if(!IsSpawned)return;
            if(tracer!=null)tracer.enabled=Time.time<flashUntil;
            float remaining=(float)(ExpiresAt.Value-NetworkManager.ServerTime.Time);
            if(timerLabel!=null){timerLabel.text="TURRET  "+Mathf.Max(0,Mathf.CeilToInt(remaining))+"s";if(Camera.main!=null)timerLabel.transform.rotation=Camera.main.transform.rotation;}
            if(!IsServer)return;
            if(remaining<=0){NetworkObject.Despawn(true);return;}
            if(!NetworkRoundGate.IsOpen || Time.time<nextShot || muzzle==null)return;
            nextShot=Time.time+1/Mathf.Max(1,shotsPerSecond);
            ZombieAI target=null;Vector3 targetPoint=default;float closest=range*range;
            foreach(var zombie in ZombieAI.ActiveZombies){
                if(zombie==null || !zombie.isActiveAndEnabled || zombie.GetComponent<Health>() is not Health hp || hp.CurrentHealth<=0)continue;
                Vector3 point=zombie.transform.position+Vector3.up;float distance=(point-muzzle.position).sqrMagnitude;
                if(distance>=closest)continue;
                if(Physics.Linecast(muzzle.position,point,out var hit,~0,QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<ZombieAI>()!=zombie)continue;
                closest=distance;target=zombie;targetPoint=point;
            }
            if(target==null)return;
            GameObject source=gameObject;
            if(NetworkManager.ConnectedClients.TryGetValue(DeployedBy.Value,out var owner) && owner.PlayerObject!=null)source=owner.PlayerObject.gameObject;
            target.GetComponent<Health>().TakeDamage(damage,source);
            ShotRpc(muzzle.position,targetPoint);
        }
        [Rpc(SendTo.ClientsAndHost)]
        void ShotRpc(Vector3 from,Vector3 to)
        {
            if(swivel!=null){var direction=to-swivel.position;direction.y=0;if(direction.sqrMagnitude>.01f)swivel.rotation=Quaternion.LookRotation(direction);}
            if(shotAudio!=null && shotAudio.clip!=null)shotAudio.PlayOneShot(shotAudio.clip);
            if(tracer!=null){tracer.SetPosition(0,from);tracer.SetPosition(1,to);tracer.enabled=true;flashUntil=Time.time+.06f;}
        }
    }
}
