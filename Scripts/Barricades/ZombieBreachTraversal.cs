using UnityEngine;
using UnityEngine.AI;
using Unity.FPS.AI;
using Unity.FPS.Game;
using ZombieTown.Multiplayer;
namespace ZombieTown.Foundation
{
    public sealed class ZombieBreachTraversal : MonoBehaviour
    {
        public BarricadeWindow window;
        ZombieAI zombie; NavMeshAgent agent; Animator animator; Health health;
        bool traversing, complete, oldPosition, oldRotation, oldRootMotion;
        float elapsed, nextAttack;
        Vector3 start, end;
        byte pose;
        void SetPose(byte next){if(pose==next)return;pose=next;GetComponent<NetworkZombieAnimator>()?.SetWindowPose(next);}
        public void Assign(BarricadeWindow next) { if(window==next && !complete)return;Cleanup();window=next;complete=false; }
        void Awake() { zombie=GetComponent<ZombieAI>(); agent=GetComponent<NavMeshAgent>(); animator=GetComponent<Animator>(); health=GetComponent<Health>(); }
        public bool Tick() {
            if(complete || window==null || !window.IsSpawned || !window.IsServer || !window.Available) return false;
            if(health==null || health.CurrentHealth<=0) { Cleanup(); return false; }
            if(!NetworkRoundGate.IsOpen) return true;
            if(agent==null || !agent.enabled || !agent.isOnNavMesh) return false;
            if(traversing) {
                elapsed+=Time.deltaTime;
                float t=Mathf.Clamp01(elapsed/Mathf.Max(1.8f,window.definition!=null?window.definition.traversalDuration:1.8f));
                transform.position=WindowClimbMotion.Position(start,end,window.traversalLift,t);
                bool climbing=t<.28f;
                SetPose(climbing?(byte)1:(byte)2);
                animator.SetBool("IsClimbing",climbing);zombie.SetCrouching(true);
                if(t>=1) {
                    agent.Warp(end-Vector3.up*zombie.GroundOffset); transform.position=end;
                    traversing=false; if(window!=null)window.Release(this); complete=true;
                    zombie.RestorePostureAfterTraversal(false);
                }
                return true;
            }
            if(window.zombieTraverseStart==null || window.zombieTraverseEnd==null) return false;
            Vector3 target=window.zombieTraverseStart.position;
            if(!window.Claim(this)) {
                target=window.zombieApproach!=null?window.zombieApproach.position:target-transform.forward*2;
                agent.stoppingDistance=1; agent.isStopped=false; agent.SetDestination(target); SyncPosition(); return true;
            }
            agent.stoppingDistance=.08f;
            if(Vector3.Distance(transform.position-Vector3.up*zombie.GroundOffset,target)>.18f) {
                agent.isStopped=false; agent.SetDestination(target); SyncPosition(); animator.SetBool("IsWalking",true); animator.SetFloat("Speed",agent.velocity.magnitude); return true;
            }
            agent.isStopped=true;
            Vector3 direction=window.zombieTraverseEnd.position-target; direction.y=0;
            if(direction.sqrMagnitude>.01f) transform.rotation=Quaternion.LookRotation(direction);
            animator.SetBool("IsWalking",false); animator.SetFloat("Speed",0);
            if(!window.IsBreached) {
                if(Time.time>=nextAttack) { nextAttack=Time.time+Mathf.Max(.2f,zombie.AttackInterval); animator.SetTrigger("Attack"); window.AttackBoard(this,zombie); }
                return true;
            }
            start=transform.position; end=window.zombieTraverseEnd.position+Vector3.up*zombie.GroundOffset;
            traversing=true; elapsed=0; oldPosition=agent.updatePosition; oldRotation=agent.updateRotation; oldRootMotion=animator.applyRootMotion;
            agent.updatePosition=false; agent.updateRotation=false; animator.applyRootMotion=false;
            zombie.SetCrouching(true);animator.SetBool("IsClimbing",true); animator.SetBool("IsWalking",true); animator.SetFloat("Speed",zombie.MoveSpeed*zombie.CrouchSpeedMultiplier);
            return true;
        }
        void SyncPosition() { if(!agent.updatePosition) transform.position=agent.nextPosition+Vector3.up*zombie.GroundOffset; }
        void Cleanup() {
            if(traversing) { if(agent!=null && agent.enabled) { agent.updatePosition=oldPosition; agent.updateRotation=oldRotation; if(agent.isOnNavMesh) agent.isStopped=false; } if(animator!=null) animator.applyRootMotion=oldRootMotion; zombie?.SetCrouching(false); }
            if(animator!=null)animator.SetBool("IsClimbing",false);
            SetPose(0);
            traversing=false; if(window!=null) window.Release(this);
        }
        void OnDisable()=>Cleanup();
    }
}
