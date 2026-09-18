using UnityEngine;

namespace ZombieTown.Multiplayer
{
    public sealed class WeaponHoldIK : MonoBehaviour
    {
        Animator animator;
        Transform playerRoot;
        PlayerArchetype archetype;
        bool meleeEquipped;
        Quaternion propLocalRotation;
        Transform handProp;
        float swingStarted = Mathf.NegativeInfinity;
        float specialChargeStarted = Mathf.NegativeInfinity;
        float specialSwingStarted = Mathf.NegativeInfinity;
        float reloadStarted = Mathf.NegativeInfinity;
        float shotStarted = Mathf.NegativeInfinity;
        bool chainsawPowered;
        int attackIndex;

        public void PlayMeleeSwing(int index)
        {
            attackIndex = Mathf.Abs(index) % 3;
            swingStarted = Time.time;
            SetAnimatorTrigger("Melee");
            SetAnimatorInteger("MeleeAttack", attackIndex);
        }

        public void PlaySpecialMeleeSwing()
        {
            specialChargeStarted = Mathf.NegativeInfinity;
            specialSwingStarted = Time.time;
            SetAnimatorTrigger("Melee");
            SetAnimatorInteger("MeleeAttack", 2);
        }

        public void SetSpecialMeleeCharging(bool charging)
        {
            specialChargeStarted = charging ? Time.time : Mathf.NegativeInfinity;
        }

        public void PlayReload()
        {
            reloadStarted = Time.time;
            SetAnimatorTrigger("Reload");
        }

        public void PlayShot() => shotStarted = Time.time;

        public void SetChainsawPowered(bool powered) => chainsawPowered = powered;

        public void Initialize(Animator targetAnimator, Transform root, PlayerArchetype type, Vector3 localPropEuler)
        {
            animator = targetAnimator;
            playerRoot = root;
            archetype = type;
            meleeEquipped = type == PlayerArchetype.Striker;
            propLocalRotation = Quaternion.Euler(localPropEuler);
        }

        public void SetMeleeEquipped(bool equipped) => meleeEquipped = equipped;

        public void SetHandProp(Transform prop, Vector3 localPropEuler)
        {
            handProp = prop;
            propLocalRotation = Quaternion.Euler(localPropEuler);
        }

        void LateUpdate()
        {
            // Humanoid hand-bone axes differ per imported character. Keeping the gun's
            // final world rotation independent of that axis prevents remote guns turning sideways.
            if (handProp == null || playerRoot == null || meleeEquipped) return;
            float recoil = Mathf.Sin(Mathf.Clamp01((Time.time - shotStarted) / .12f) * Mathf.PI);
            float sawShake = chainsawPowered ? Mathf.Sin(Time.time * 48f) * 1.4f : 0f;
            handProp.rotation = Quaternion.LookRotation(playerRoot.forward, playerRoot.up) *
                                Quaternion.Euler(-recoil * 7f, 0f, sawShake);
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || playerRoot == null || !animator.isHuman) return;

            bool melee = meleeEquipped;
            float specialRatio = Mathf.Clamp01((Time.time - specialSwingStarted) / 1.08f);
            bool specialSwing = melee && Time.time - specialSwingStarted <= 1.08f;
            float chargeRatio = Mathf.Clamp01((Time.time - specialChargeStarted) / 1f);
            bool specialCharging = melee && specialChargeStarted > Mathf.NegativeInfinity && !specialSwing;
            float swing = melee && !specialSwing ? Mathf.Sin(Mathf.Clamp01((Time.time - swingStarted) / (attackIndex == 2 ? .58f : .46f)) * Mathf.PI) : 0f;
            float side = attackIndex == 1 ? 1f : -1f;
            float overhead = attackIndex == 2 ? swing : 0f;
            float reload = !melee ? Mathf.Sin(Mathf.Clamp01((Time.time - reloadStarted) / 1.15f) * Mathf.PI) : 0f;
            float shotRecoil = !melee ? Mathf.Sin(Mathf.Clamp01((Time.time - shotStarted) / .12f) * Mathf.PI) : 0f;
            float specialSweep = Mathf.Clamp01((specialRatio - .12f) / .72f);
            float specialAngle = Mathf.SmoothStep(0f, 1f, specialSweep) * Mathf.PI * 2.55f;
            Vector3 rightTarget = specialSwing
                ? playerRoot.position + playerRoot.up * (1.12f + .22f * Mathf.Sin(specialRatio * Mathf.PI)) +
                   playerRoot.right * (Mathf.Cos(specialAngle) * .9f) +
                   playerRoot.forward * (Mathf.Sin(specialAngle) * .9f)
                : specialCharging
                ? playerRoot.position + playerRoot.up * (1.3f + .04f * Mathf.Sin(chargeRatio * Mathf.PI * 7f)) -
                  playerRoot.right * .48f - playerRoot.forward * .1f
                : melee
                ? playerRoot.position + playerRoot.up * (1.08f + .18f * swing + .22f * overhead) + playerRoot.right * (.28f + side * .34f * swing) + playerRoot.forward * (.2f + .3f * swing)
                : playerRoot.position + playerRoot.up * (1.25f - .22f * reload) + playerRoot.right * (.14f - .08f * reload) + playerRoot.forward * (.43f - .12f * reload - .06f * shotRecoil);
            // Counter-rotate the model's local import orientation. This makes the
            // weapon barrel point where the player looks while the palm owns the grip.
            Quaternion desiredPropRotation = specialSwing
                ? Quaternion.AngleAxis(Mathf.SmoothStep(0f, 1f, specialSweep) * 520f, playerRoot.up) *
                  Quaternion.LookRotation(playerRoot.forward, playerRoot.up) * Quaternion.Euler(-68f, 0f, 82f)
                : specialCharging
                ? Quaternion.LookRotation(playerRoot.forward, playerRoot.up) * Quaternion.Euler(-28f, -52f, -95f)
                : Quaternion.LookRotation(playerRoot.forward, playerRoot.up) *
                  Quaternion.Euler(-62f * swing - 28f * overhead + 32f * reload, 16f * side * swing, -58f * side * swing - 24f * reload);
            Quaternion handRotation = desiredPropRotation * Quaternion.Inverse(propLocalRotation);

            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
            animator.SetIKPosition(AvatarIKGoal.RightHand, rightTarget);
            animator.SetIKRotation(AvatarIKGoal.RightHand, handRotation);

            float leftWeight = melee ? 0f : 1f;
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftWeight);
            if (!melee)
            {
                Vector3 leftTarget = playerRoot.position + playerRoot.up * 1.22f - playerRoot.right * .07f + playerRoot.forward * .4f;
                leftTarget += -playerRoot.up * (.18f * reload) + playerRoot.right * (.1f * reload) - playerRoot.forward * (.1f * reload);
                animator.SetIKPosition(AvatarIKGoal.LeftHand, leftTarget);
                animator.SetIKRotation(AvatarIKGoal.LeftHand, handRotation);
            }

            animator.SetLookAtWeight(.65f, .2f, .8f, .2f, .5f);
            animator.SetLookAtPosition(playerRoot.position + playerRoot.up * 1.45f + playerRoot.forward * 8f);
        }

        void SetAnimatorTrigger(string parameterName)
        {
            if (animator == null) return;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.name == parameterName && parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger(parameterName);
                    return;
                }
        }

        void SetAnimatorInteger(string parameterName, int value)
        {
            if (animator == null) return;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.name == parameterName && parameter.type == AnimatorControllerParameterType.Int)
                {
                    animator.SetInteger(parameterName, value);
                    return;
                }
        }
    }
}
