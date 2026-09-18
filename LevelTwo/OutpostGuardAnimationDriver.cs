using UnityEngine;

namespace ZombieTown.LevelTwo
{
    /// <summary>
    /// Maps guard gameplay onto the same minimal parameter contract as the player
    /// locomotion controller. Optional guard parameters can be added later without
    /// changing the AI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutpostGuardAnimationDriver : MonoBehaviour
    {
        [SerializeField] OutpostGuardAI guard;
        [SerializeField] Animator animator;
        [Header("Player-compatible parameters")]
        [SerializeField] string speedParameter = "Speed";
        [SerializeField] string groundedParameter = "Grounded";
        [SerializeField] string crouchedParameter = "Crouched";
        [Header("Optional custom guard parameters")]
        [SerializeField] string suspiciousParameter = "Suspicious";
        [SerializeField] string alertParameter = "Alert";
        [SerializeField] string aimingParameter = "Aiming";
        [SerializeField] string isDeadParameter = "IsDead";
        [SerializeField] string attackTrigger = "Attack";
        [SerializeField] string reloadTrigger = "Reload";
        [SerializeField] string hitTrigger = "Hit";
        [SerializeField] string knockbackTrigger = "Knockback";
        [SerializeField] string meleeTrigger = "Melee";
        [SerializeField] string dieTrigger = "Die";
        [SerializeField] string headshotDieTrigger = "HeadshotDie";
        [Header("Death playback")]
        [SerializeField, Range(0f, .9f)] float deathStartOffset = .42f;
        [SerializeField, Range(0f, .9f)] float headshotDeathStartOffset = .42f;
        [SerializeField] string dieStateName = "Base Layer.Die";
        [SerializeField] string headshotDieStateName = "Base Layer.HeadshotDie";

        float movementSpeed;
        bool grounded = true;
        bool crouched;

        public void Initialize(OutpostGuardAI targetGuard, Animator targetAnimator)
        {
            guard = targetGuard;
            animator = targetAnimator;
            ApplyState();
        }

        void LateUpdate() => ApplyState();

        public void PlayAttack()
        {
            SetTrigger(attackTrigger);
        }

        public void PlayReload() => SetTrigger(reloadTrigger);
        public void PlayHit() => SetTrigger(hitTrigger);
        public void PlayKnockback() => SetTrigger(knockbackTrigger);
        public void PlayMelee() => SetTrigger(meleeTrigger);

        public void SetMovement(float speed, bool isGrounded = true)
        {
            movementSpeed = Mathf.Max(0f, speed);
            grounded = isGrounded;
        }

        public void SetCrouched(bool value) => crouched = value;

        public void SetDead(bool headshot = false)
        {
            if (animator == null) return;
            SetBool(isDeadParameter, true);
            SetBool(suspiciousParameter, false);
            SetBool(alertParameter, false);
            SetBool(aimingParameter, false);
            SetMovement(0f);
            animator.ResetTrigger(attackTrigger);
            animator.ResetTrigger(reloadTrigger);
            animator.ResetTrigger(hitTrigger);
            animator.ResetTrigger(knockbackTrigger);
            animator.ResetTrigger(meleeTrigger);
            string stateName = headshot ? headshotDieStateName : dieStateName;
            int state = Animator.StringToHash(stateName);
            if (animator.HasState(0, state))
            {
                animator.speed = 1f;
                animator.Play(state, 0, headshot ? headshotDeathStartOffset : deathStartOffset);
                animator.Update(0f);
            }
            else
            {
                SetTrigger(headshot ? headshotDieTrigger : dieTrigger);
            }
        }

        void ApplyState()
        {
            if (guard == null || animator == null) return;
            OutpostGuardAI.GuardState state = guard.CurrentState;
            bool moving = movementSpeed > .1f;
            SetFloat(speedParameter, movementSpeed);
            SetBool(groundedParameter, grounded);
            SetBool(crouchedParameter, crouched);
            // Suspicious/Alert are stationary upper-body pose slots. While the
            // agent moves, Speed must be allowed to select Walk/Run instead.
            SetBool(suspiciousParameter, !moving && state == OutpostGuardAI.GuardState.Suspicious);
            SetBool(alertParameter, !moving && (state == OutpostGuardAI.GuardState.Alert ||
                                                state == OutpostGuardAI.GuardState.Engaging));
            SetBool(aimingParameter, state == OutpostGuardAI.GuardState.Suspicious ||
                                     state == OutpostGuardAI.GuardState.Alert ||
                                     state == OutpostGuardAI.GuardState.Engaging);
            SetBool(isDeadParameter, state == OutpostGuardAI.GuardState.Dead);
        }

        void SetTrigger(string parameter)
        {
            if (HasParameter(parameter, AnimatorControllerParameterType.Trigger)) animator.SetTrigger(parameter);
        }

        void SetFloat(string parameter, float value)
        {
            if (HasParameter(parameter, AnimatorControllerParameterType.Float)) animator.SetFloat(parameter, value);
        }

        void SetBool(string parameter, bool value)
        {
            if (HasParameter(parameter, AnimatorControllerParameterType.Bool)) animator.SetBool(parameter, value);
        }

        bool HasParameter(string parameter, AnimatorControllerParameterType type)
        {
            if (animator == null || string.IsNullOrEmpty(parameter)) return false;
            foreach (AnimatorControllerParameter candidate in animator.parameters)
                if (candidate.name == parameter && candidate.type == type)
                    return true;
            return false;
        }
    }
}
