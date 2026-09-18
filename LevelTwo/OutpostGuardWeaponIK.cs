using UnityEngine;

namespace ZombieTown.LevelTwo
{
    /// <summary>
    /// Keeps the weapon on a stable character-space mount and brings both hands
    /// to its grips. This avoids depending on vendor-specific hand bone axes.
    /// </summary>
    public sealed class OutpostGuardWeaponIK : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] Transform guardRoot;
        [SerializeField] Transform weaponMount;
        [Header("Weapon rig")]
        [SerializeField] Vector3 weaponPosition = new(.16f, 1.3f, .3f);
        [SerializeField] Vector3 rightGripPosition = Vector3.zero;
        [SerializeField] Vector3 leftGripPosition = new(-.035f, -.025f, .29f);
        [SerializeField, Range(0f, 1f)] float handRotationWeight = .65f;

        Vector3 aimPoint;
        float recoilStarted = float.NegativeInfinity;
        bool hasAimPoint;
        bool dead;
        bool resolved;
        bool rotationsCalibrated;
        Quaternion rightHandRotationOffset = Quaternion.identity;
        Quaternion leftHandRotationOffset = Quaternion.identity;

        public Transform WeaponMount => weaponMount;

        public void Initialize(Animator targetAnimator, Transform root, Transform mount = null)
        {
            animator = targetAnimator;
            guardRoot = root;
            if (mount != null) weaponMount = mount;
            resolved = false;
            ResolveRig();
        }

        void Awake() => ResolveRig();

        public void SetAimPoint(Vector3 point)
        {
            aimPoint = point;
            hasAimPoint = true;
        }

        public void ClearAimPoint() => hasAimPoint = false;

        public void PlayShot(Vector3 target)
        {
            SetAimPoint(target);
            recoilStarted = Time.time;
        }

        public void SetDead()
        {
            dead = true;
            hasAimPoint = false;
            Transform hand = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.RightHand)
                : null;
            if (weaponMount != null && hand != null) weaponMount.SetParent(hand, true);
        }

        void OnAnimatorIK(int layerIndex)
        {
            ResolveRig();
            if (dead || animator == null || guardRoot == null || weaponMount == null || !animator.isHuman)
                return;

            Vector3 forward = hasAimPoint ? aimPoint - weaponMount.position : guardRoot.forward;
            forward.y = Mathf.Clamp(forward.y, -.45f, .45f);
            if (forward.sqrMagnitude < .001f) forward = guardRoot.forward;
            forward.Normalize();

            float recoilTime = Time.time - recoilStarted;
            float recoil = recoilTime >= 0f && recoilTime <= .2f
                ? Mathf.Sin(recoilTime / .2f * Mathf.PI)
                : 0f;
            Vector3 up = guardRoot.up;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Quaternion weaponRotation = Quaternion.LookRotation(forward, up);
            Vector3 mountPosition = guardRoot.position + right * weaponPosition.x + up * weaponPosition.y +
                                    forward * (weaponPosition.z - recoil * .09f);
            weaponMount.SetPositionAndRotation(mountPosition, weaponRotation);

            Transform rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform leftHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (!rotationsCalibrated && rightHandBone != null && leftHandBone != null)
            {
                rightHandRotationOffset = Quaternion.Inverse(guardRoot.rotation) * rightHandBone.rotation;
                leftHandRotationOffset = Quaternion.Inverse(guardRoot.rotation) * leftHandBone.rotation;
                rotationsCalibrated = true;
            }

            Vector3 rightHand = weaponMount.TransformPoint(rightGripPosition);
            Vector3 leftHand = weaponMount.TransformPoint(leftGripPosition + Vector3.forward * (recoil * .02f));
            Quaternion rightRotation = weaponRotation * rightHandRotationOffset;
            Quaternion leftRotation = weaponRotation * leftHandRotationOffset;

            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, handRotationWeight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, rightHand);
            animator.SetIKRotation(AvatarIKGoal.RightHand, rightRotation);
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, handRotationWeight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHand);
            animator.SetIKRotation(AvatarIKGoal.LeftHand, leftRotation);

            Vector3 shoulder = guardRoot.position + up * 1.3f;
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, .7f);
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, shoulder + right * .48f - forward * .08f);
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, .7f);
            animator.SetIKHintPosition(AvatarIKHint.LeftElbow, shoulder - right * .42f + forward * .06f);

            animator.SetLookAtWeight(.75f, .2f, .85f, .2f, .55f);
            animator.SetLookAtPosition(hasAimPoint ? aimPoint : guardRoot.position + up * 1.45f + forward * 10f);
        }

        void ResolveRig()
        {
            if (resolved || animator == null || guardRoot == null) return;
            if (weaponMount == null) weaponMount = FindChild(guardRoot, "Weapon Mount");
            if (weaponMount == null) return;
            Transform weaponVisual = FindChild(weaponMount, "Spy Guard MP5");
            if (weaponVisual != null)
                weaponVisual.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (weaponMount.parent != guardRoot) weaponMount.SetParent(guardRoot, true);
            Transform muzzle = FindChild(weaponMount, "Muzzle");
            if (muzzle != null && muzzle.localPosition.z < 0f)
            {
                Vector3 muzzlePosition = muzzle.localPosition;
                muzzlePosition.z = -muzzlePosition.z;
                muzzle.localPosition = muzzlePosition;
                muzzle.localRotation = Quaternion.identity;
            }
            resolved = true;
        }

        static Transform FindChild(Transform root, string targetName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == targetName) return child;
            return null;
        }
    }
}
