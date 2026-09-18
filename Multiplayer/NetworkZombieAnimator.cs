using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using Unity.FPS.Game;

namespace ZombieTown.Multiplayer
{
    [RequireComponent(typeof(Animator))]
    public sealed class NetworkZombieAnimator : NetworkBehaviour
    {
        static readonly int Hit = Animator.StringToHash("Hit");
        static readonly int OnDamaged = Animator.StringToHash("OnDamaged");
        static readonly int Attack = Animator.StringToHash("Attack");
        Animator animator;
        Health health;
        public readonly NetworkVariable<float> SyncedHealth = new();
        public readonly NetworkVariable<byte> WindowPose = new();
        byte appliedWindowPose;
        public readonly NetworkVariable<NetworkObjectReference> BusGripWindow = new();
        public readonly NetworkVariable<bool> BusGripOnRoof=new();
        public void SetBusGrip(ZombieTown.Foundation.BarricadeWindow window,bool roof=false){if(IsServer && IsSpawned){BusGripWindow.Value=window!=null?new NetworkObjectReference(window.NetworkObject):default;BusGripOnRoof.Value=roof;}}
        void OnAnimatorIK(int layer){
            if(animator==null)return;
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand,0);animator.SetIKPositionWeight(AvatarIKGoal.RightHand,0);
            if(appliedWindowPose!=4 || !BusGripWindow.Value.TryGet(out var net))return;
            var zone=net.GetComponent<ZombieTown.Foundation.BusWindowAttachment>()?.Zone;if(zone==null)return;
            var box=zone.Box;
            Vector3 top=box.center+new Vector3(0,box.size.y*.5f,-box.size.z*.5f);
            if(BusGripOnRoof.Value && zone.VehicleRoot.TryGetComponent<ZombieTown.Foundation.BusDefenseLayout>(out var layout)){
                Vector3 world=zone.transform.TransformPoint(top);world.y=zone.VehicleRoot.TransformPoint(Vector3.up*layout.roofFloorHeight).y;top=zone.transform.InverseTransformPoint(world);
            }
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand,1);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand,animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.BusHangAttack")?.25f:1);
            animator.SetIKPosition(AvatarIKGoal.LeftHand,zone.transform.TransformPoint(top+Vector3.left*box.size.x*.2f));
            animator.SetIKPosition(AvatarIKGoal.RightHand,zone.transform.TransformPoint(top+Vector3.right*box.size.x*.2f));
        }
        public void SetWindowPose(byte pose){if(IsServer && IsSpawned)WindowPose.Value=pose;ApplyWindowPose(0,pose);}
        void ApplyWindowPose(byte before,byte pose){
            if(animator==null)return;
            before=appliedWindowPose;appliedWindowPose=pose;
            animator.SetBool("IsClimbing",pose==1);animator.SetBool("IsCrouching",pose==2 || pose==5);
            string state=pose==2?"CrouchIdle":pose==3?"BusJump":pose==4?"BusHang":pose==5?"BusCrawl":pose==6?"Idle":null;
            if(state!=null && animator.HasState(0,Animator.StringToHash("Base Layer."+state)))animator.CrossFadeInFixedTime("Base Layer."+state,pose==6?.75f:.15f);
            else if(before>=3 && pose==0 && (health==null || health.CurrentHealth>0))animator.CrossFadeInFixedTime("Base Layer.Idle",.1f);
        }
        public void PlayBusBoardAttack(){if(IsSpawned && IsServer)BusBoardAttackRpc();}
        [Rpc(SendTo.Everyone)] void BusBoardAttackRpc(){
            if(animator!=null && animator.HasState(0,Animator.StringToHash("Base Layer.BusHangAttack")))animator.CrossFadeInFixedTime("Base Layer.BusHangAttack",.06f);
        }
        RuntimeAnimatorController baseController;
        Coroutine knockbackRoutine;
        Vector3 previousPosition;
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int IsWalking = Animator.StringToHash("IsWalking");
        static readonly int IsDead = Animator.StringToHash("IsDead");
        [Tooltip("Optional replacement clip for the Striker knockback/fall-back reaction. Assign any compatible clip here on a zombie prefab.")]
        public AnimationClip KnockbackAnimation;
        [Range(0f, .9f)] public float KnockbackAnimationStartOffset = .42f;
        AnimatorOverrideController knockbackOverride;

        void Awake()
        {
            animator = GetComponent<Animator>();
            health = GetComponent<Health>();
            if (animator != null) baseController = animator.runtimeAnimatorController;
            previousPosition = transform.position;
        }

        void Update()
        {
            if (animator == null || IsServer) return;
            float speed = (transform.position - previousPosition).magnitude / Mathf.Max(.001f, Time.deltaTime);
            previousPosition = transform.position;
            animator.SetFloat(Speed, speed, .12f, Time.deltaTime);
            animator.SetBool(IsWalking, speed > .05f);
        }

        public override void OnNetworkSpawn()
        {
            SyncedHealth.OnValueChanged += OnSyncedHealthChanged;
            WindowPose.OnValueChanged+=ApplyWindowPose;
            if(WindowPose.Value!=0)ApplyWindowPose(0,WindowPose.Value);
            if (health != null) health.OnDamaged += SyncHealth;
            if (health != null) health.OnHealed += SyncHealth;
            if (IsServer && health != null) SyncedHealth.Value = health.CurrentHealth;
        }

        public override void OnNetworkDespawn()
        {
            SyncedHealth.OnValueChanged -= OnSyncedHealthChanged;
            WindowPose.OnValueChanged-=ApplyWindowPose;
            if (health != null) health.OnDamaged -= SyncHealth;
            if (health != null) health.OnHealed -= SyncHealth;
        }

        void SyncHealth(float _)
        {
            if (IsServer && health != null) SyncedHealth.Value = health.CurrentHealth;
        }

        void SyncHealth(float _, GameObject source) => SyncHealth(0f);

        void OnSyncedHealthChanged(float previous, float current)
        {
            if (IsServer) return;
            if (health != null) health.CurrentHealth = current;
            if (current <= 0f && animator != null)
            {
                animator.SetBool(IsDead, true);
                animator.CrossFadeInFixedTime(Animator.StringToHash("Base Layer.Die"), .04f);
            }
        }

        public void PlayHit()
        {
            if (!IsSpawned)
            {
                ApplyHit();
                return;
            }
            if (IsServer) PlayHitRpc();
        }

        [Rpc(SendTo.Everyone)]
        void PlayHitRpc() => ApplyHit();

        void ApplyHit()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) return;

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Trigger) continue;
                if (parameter.nameHash == Hit) { animator.SetTrigger(Hit); return; }
                if (parameter.nameHash == OnDamaged) { animator.SetTrigger(OnDamaged); return; }
            }
        }

        public void PlayKnockback(float recoveryDuration)
        {
            if (!IsSpawned)
            {
                ApplyKnockback(recoveryDuration);
                return;
            }
            if (IsServer) PlayKnockbackRpc(recoveryDuration);
        }

        public void PlayAttack()
        {
            if (!IsSpawned)
            {
                ApplyAttack();
                return;
            }
            if (IsServer) PlayAttackRpc();
        }

        [Rpc(SendTo.Everyone)]
        void PlayAttackRpc() => ApplyAttack();

        void ApplyAttack()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) return;
            animator.ResetTrigger(Attack);
            animator.SetTrigger(Attack);
        }

        [Rpc(SendTo.Everyone)]
        void PlayKnockbackRpc(float recoveryDuration) => ApplyKnockback(recoveryDuration);

        void ApplyKnockback(float recoveryDuration)
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) return;
            ApplyManualKnockbackOverride();
            int state = Animator.StringToHash("Base Layer.Knockback");
            if (animator.HasState(0, state))
                animator.CrossFadeInFixedTime(state, .04f, 0, KnockbackAnimationStartOffset);
            else
                animator.CrossFadeInFixedTime(Animator.StringToHash("Base Layer.Die"), .04f, 0,
                    KnockbackAnimationStartOffset);
            if (knockbackRoutine != null) StopCoroutine(knockbackRoutine);
            knockbackRoutine = StartCoroutine(ResumeAfterKnockback(recoveryDuration));
        }

        void ApplyManualKnockbackOverride()
        {
            if (KnockbackAnimation == null || knockbackOverride != null ||
                animator.runtimeAnimatorController == null) return;

            knockbackOverride = new AnimatorOverrideController(animator.runtimeAnimatorController);
            List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new();
            knockbackOverride.GetOverrides(overrides);
            bool replaced = false;
            for (int i = 0; i < overrides.Count; i++)
            {
                AnimationClip original = overrides[i].Key;
                if (original == null || (original.name != "Zombie_Knockback" && original.name != "Zombie_Die"))
                    continue;
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, KnockbackAnimation);
                replaced = true;
            }
            if (!replaced) return;
            knockbackOverride.ApplyOverrides(overrides);
            animator.runtimeAnimatorController = knockbackOverride;
        }

        IEnumerator ResumeAfterKnockback(float recoveryDuration)
        {
            yield return new WaitForSeconds(Mathf.Max(.75f, recoveryDuration));
            if (animator != null && (health == null || health.CurrentHealth > 0f))
            {
                if (baseController != null && animator.runtimeAnimatorController != baseController)
                    animator.runtimeAnimatorController = baseController;
                animator.CrossFadeInFixedTime(Animator.StringToHash("Base Layer.Idle"), .1f);
            }
            knockbackOverride = null;
            knockbackRoutine = null;
        }
    }
}
