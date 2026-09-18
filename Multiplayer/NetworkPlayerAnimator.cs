using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace ZombieTown.Multiplayer
{
    public sealed class NetworkPlayerAnimator : NetworkBehaviour
    {
        [SerializeField] Animator animator;
        PlayerCharacterController controller;
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int Grounded = Animator.StringToHash("Grounded");
        static readonly int Crouched = Animator.StringToHash("Crouched");
        public readonly NetworkVariable<float> MoveSpeed = new(writePerm: NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> IsGrounded = new(writePerm: NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> IsCrouched = new(writePerm: NetworkVariableWritePermission.Owner);
        void Awake() => controller = GetComponent<PlayerCharacterController>();
        void Update()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (IsOwner && controller != null) { MoveSpeed.Value = controller.CharacterVelocity.magnitude; IsGrounded.Value = controller.IsGrounded; IsCrouched.Value = controller.IsCrouching; }
            if (animator != null) { animator.SetFloat(Speed, MoveSpeed.Value, .12f, Time.deltaTime); animator.SetBool(Grounded, IsGrounded.Value); animator.SetBool(Crouched, IsCrouched.Value); }
        }
    }
}
