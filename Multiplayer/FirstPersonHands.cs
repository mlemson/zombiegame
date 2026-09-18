using System.Collections;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;
using ZombieTown.Weapons;

namespace ZombieTown.Multiplayer
{
    public sealed class FirstPersonHands : MonoBehaviour
    {
        GameObject classProp;
        Vector3 classPropRestPosition;
        Quaternion classPropRestRotation;
        Coroutine swingRoutine;
        bool specialChargeActive;
        GameObject armsInstance;
        GameObject armsPrefab;
        Renderer[] armsRenderers;
        FirstPersonArmsRig armsRig;

        public static FirstPersonHands Ensure(PlayerWeaponsManager weapons)
        {
            if (weapons == null || weapons.WeaponParentSocket == null) return null;
            FirstPersonHands existing = weapons.WeaponParentSocket.GetComponent<FirstPersonHands>();
            if (existing != null) return existing;
            return weapons.WeaponParentSocket.gameObject.AddComponent<FirstPersonHands>();
        }

        public void ApplyClass(PlayerArchetype type, GameObject prefab,
            RuntimeAnimatorController animatorController, Vector3 localPosition,
            Vector3 localEulerAngles, float localScale)
        {
            if (prefab == null) return;
            PlayerWeaponsManager weapons = GetComponentInParent<PlayerWeaponsManager>();
            if (weapons == null) return;

            if (armsInstance == null || armsPrefab != prefab)
            {
                if (armsInstance != null) Destroy(armsInstance);
                armsPrefab = prefab;
                armsInstance = Instantiate(prefab, transform);
                armsInstance.name = "First Person Arms";

                foreach (Collider collider in armsInstance.GetComponentsInChildren<Collider>(true))
                    Destroy(collider);
                armsRenderers = armsInstance.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in armsRenderers)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                SetLayerRecursively(armsInstance.transform, gameObject.layer);

                Animator animator = armsInstance.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    if (animatorController != null) animator.runtimeAnimatorController = animatorController;
                    armsRig = animator.GetComponent<FirstPersonArmsRig>() ??
                              animator.gameObject.AddComponent<FirstPersonArmsRig>();
                    armsRig.Initialize(animator, weapons, type, armsRenderers);
                }
            }

            // Older player prefabs serialized the arms exactly in the screen centre.
            // Keep authored offsets, but migrate that legacy value beside the weapon.
            if (localPosition.x < .28f) localPosition.x = .34f;
            if (localPosition.y < -1.3f) localPosition.y = -1.2f;
            armsInstance.transform.SetLocalPositionAndRotation(localPosition, Quaternion.Euler(localEulerAngles));
            armsInstance.transform.localScale = Vector3.one * Mathf.Max(.1f, localScale);
            armsInstance.SetActive(true);
            armsRig?.SetArchetype(type);
        }

        static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayerRecursively(child, layer);
        }

        public void ApplyClassProp(PlayerArchetype type, GameObject propPrefab)
        {
            // The sword is now a real WeaponController inventory item. Keeping a
            // second class-only prop here caused it to disappear or overlap guns.
            classProp = null;
        }

        static void NormalizeLongestSide(GameObject model, float targetLength)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest > .001f) model.transform.localScale *= targetLength / longest;
        }

        public void PlayMeleeSwing(int attackIndex, GameObject equippedWeaponRoot)
        {
            if (equippedWeaponRoot == null) return;
            if (swingRoutine != null)
            {
                StopCoroutine(swingRoutine);
                RestoreSwordPose();
            }
            specialChargeActive = false;
            classProp = equippedWeaponRoot;
            classPropRestPosition = classProp.transform.localPosition;
            classPropRestRotation = classProp.transform.localRotation;
            swingRoutine = StartCoroutine(SwingSword(Mathf.Abs(attackIndex) % 3));
        }

        public void BeginSpecialMeleeCharge(GameObject equippedWeaponRoot, float chargeDuration)
        {
            if (equippedWeaponRoot == null) return;
            if (swingRoutine != null)
            {
                StopCoroutine(swingRoutine);
                RestoreSwordPose();
            }
            classProp = equippedWeaponRoot;
            classPropRestPosition = classProp.transform.localPosition;
            classPropRestRotation = classProp.transform.localRotation;
            specialChargeActive = true;
            swingRoutine = StartCoroutine(ChargeSword(Mathf.Max(.2f, chargeDuration)));
        }

        public void CancelSpecialMeleeCharge()
        {
            if (!specialChargeActive) return;
            if (swingRoutine != null) StopCoroutine(swingRoutine);
            RestoreSwordPose();
            specialChargeActive = false;
            swingRoutine = null;
            classProp = null;
        }

        public void PlaySpecialMeleeSwing(GameObject equippedWeaponRoot)
        {
            if (equippedWeaponRoot == null) return;
            if (!specialChargeActive || classProp != equippedWeaponRoot)
            {
                classProp = equippedWeaponRoot;
                classPropRestPosition = classProp.transform.localPosition;
                classPropRestRotation = classProp.transform.localRotation;
            }
            if (swingRoutine != null) StopCoroutine(swingRoutine);
            specialChargeActive = false;
            swingRoutine = StartCoroutine(SpinSword());
        }

        public void SetMeleeMode(bool active)
        {
            // Weapon visibility is owned by PlayerWeaponsManager. classProp is only
            // a temporary animation target; reactivating it here made the previous
            // sword appear through a newly equipped chainsaw.
            if (swingRoutine != null) StopCoroutine(swingRoutine);
            RestoreSwordPose();
            swingRoutine = null;
            specialChargeActive = false;
            classProp = null;
        }

        public void PlaySwordTakedown(GameObject equippedWeaponRoot)
        {
            if (equippedWeaponRoot == null) return;
            if (swingRoutine != null)
            {
                StopCoroutine(swingRoutine);
                RestoreSwordPose();
            }
            specialChargeActive = false;
            classProp = equippedWeaponRoot;
            classPropRestPosition = classProp.transform.localPosition;
            classPropRestRotation = classProp.transform.localRotation;
            swingRoutine = StartCoroutine(SwordTakedown());
        }

        IEnumerator SwingSword(int attackIndex)
        {
            float duration = attackIndex == 2 ? .58f : .46f;
            Vector3 windupPosition;
            Vector3 strikePosition;
            Quaternion windupRotation;
            Quaternion strikeRotation;

            switch (attackIndex)
            {
                case 1: // fast reverse slash
                    windupPosition = classPropRestPosition + new Vector3(-.18f, .1f, -.08f);
                    strikePosition = classPropRestPosition + new Vector3(.3f, .01f, .2f);
                    windupRotation = classPropRestRotation * Quaternion.Euler(-8f, -22f, -58f);
                    strikeRotation = classPropRestRotation * Quaternion.Euler(-62f, 18f, -82f);
                    break;
                case 2: // slower overhead chop
                    windupPosition = classPropRestPosition + new Vector3(0f, .27f, -.12f);
                    strikePosition = classPropRestPosition + new Vector3(0f, -.2f, .29f);
                    windupRotation = classPropRestRotation * Quaternion.Euler(-32f, 0f, 8f);
                    strikeRotation = classPropRestRotation * Quaternion.Euler(-112f, 0f, 4f);
                    break;
                default: // diagonal slash
                    windupPosition = classPropRestPosition + new Vector3(.2f, .1f, -.07f);
                    strikePosition = classPropRestPosition + new Vector3(-.28f, .13f, .23f);
                    windupRotation = classPropRestRotation * Quaternion.Euler(-5f, 20f, 62f);
                    strikeRotation = classPropRestRotation * Quaternion.Euler(-78f, -18f, 78f);
                    break;
            }

            float elapsed = 0f;
            while (elapsed < duration && classProp != null)
            {
                elapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                if (normalized < .28f)
                {
                    float t = Mathf.SmoothStep(0f, 1f, normalized / .28f);
                    classProp.transform.localPosition = Vector3.Lerp(classPropRestPosition, windupPosition, t);
                    classProp.transform.localRotation = Quaternion.Slerp(classPropRestRotation, windupRotation, t);
                }
                else if (normalized < .68f)
                {
                    float t = Mathf.SmoothStep(0f, 1f, (normalized - .28f) / .4f);
                    classProp.transform.localPosition = Vector3.Lerp(windupPosition, strikePosition, t);
                    classProp.transform.localRotation = Quaternion.Slerp(windupRotation, strikeRotation, t);
                }
                else
                {
                    float t = Mathf.SmoothStep(0f, 1f, (normalized - .68f) / .32f);
                    classProp.transform.localPosition = Vector3.Lerp(strikePosition, classPropRestPosition, t);
                    classProp.transform.localRotation = Quaternion.Slerp(strikeRotation, classPropRestRotation, t);
                }
                yield return null;
            }
            RestoreSwordPose();
            classProp = null;
            swingRoutine = null;
        }

        IEnumerator SwordTakedown()
        {
            const float duration = .4f;
            Vector3 windupPosition = classPropRestPosition + new Vector3(.28f, .3f, -.18f);
            Vector3 strikePosition = classPropRestPosition + new Vector3(-.38f, -.14f, .48f);
            Quaternion windupRotation = classPropRestRotation * Quaternion.Euler(-24f, -32f, -98f);
            Quaternion strikeRotation = classPropRestRotation * Quaternion.Euler(-128f, 18f, 105f);
            float elapsed = 0f;
            while (elapsed < duration && classProp != null)
            {
                elapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                if (normalized < .24f)
                {
                    float t = Mathf.SmoothStep(0f, 1f, normalized / .24f);
                    classProp.transform.localPosition = Vector3.Lerp(classPropRestPosition, windupPosition, t);
                    classProp.transform.localRotation = Quaternion.Slerp(classPropRestRotation, windupRotation, t);
                }
                else if (normalized < .72f)
                {
                    float t = Mathf.SmoothStep(0f, 1f, (normalized - .24f) / .48f);
                    classProp.transform.localPosition = Vector3.Lerp(windupPosition, strikePosition, t);
                    classProp.transform.localRotation = Quaternion.Slerp(windupRotation, strikeRotation, t);
                }
                else
                {
                    float t = Mathf.SmoothStep(0f, 1f, (normalized - .72f) / .28f);
                    classProp.transform.localPosition = Vector3.Lerp(strikePosition, classPropRestPosition, t);
                    classProp.transform.localRotation = Quaternion.Slerp(strikeRotation, classPropRestRotation, t);
                }
                yield return null;
            }
            RestoreSwordPose();
            classProp = null;
            swingRoutine = null;
        }

        IEnumerator ChargeSword(float duration)
        {
            Vector3 windupPosition = classPropRestPosition + new Vector3(.32f, .25f, -.24f);
            Quaternion windupRotation = classPropRestRotation * Quaternion.Euler(-25f, -55f, -105f);
            float elapsed = 0f;
            while (elapsed < duration && classProp != null && specialChargeActive)
            {
                elapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float windup = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(normalized / .38f));
                float pulse = normalized > .38f ? Mathf.Sin((normalized - .38f) * Mathf.PI * 7f) * .018f : 0f;
                classProp.transform.localPosition = Vector3.Lerp(classPropRestPosition, windupPosition, windup) +
                    new Vector3(0f, pulse, 0f);
                classProp.transform.localRotation = Quaternion.Slerp(classPropRestRotation, windupRotation, windup) *
                    Quaternion.Euler(0f, 0f, pulse * 180f);
                yield return null;
            }
            swingRoutine = null;
        }

        IEnumerator SpinSword()
        {
            const float duration = 1.08f;
            Vector3 spinStartPosition = classProp != null ? classProp.transform.localPosition : classPropRestPosition;
            Quaternion spinStartRotation = classProp != null ? classProp.transform.localRotation : classPropRestRotation;
            float elapsed = 0f;
            while (elapsed < duration && classProp != null)
            {
                elapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float sweepProgress = Mathf.Clamp01((normalized - .12f) / .72f);
                float recovery = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((normalized - .84f) / .16f));
                float sweep = Mathf.SmoothStep(0f, 1f, sweepProgress);
                float angle = sweep * Mathf.PI * 2f;
                Vector3 orbitPosition = classPropRestPosition +
                    new Vector3(.5f * Mathf.Cos(angle), .18f + .07f * Mathf.Sin(angle * 2f),
                        .12f + .46f * Mathf.Sin(angle));
                Quaternion orbitRotation = classPropRestRotation *
                    Quaternion.Euler(-92f, sweep * 520f, 105f);
                if (normalized < .12f)
                {
                    float intro = Mathf.SmoothStep(0f, 1f, normalized / .12f);
                    classProp.transform.localPosition = Vector3.Lerp(spinStartPosition, orbitPosition, intro);
                    classProp.transform.localRotation = Quaternion.Slerp(spinStartRotation, orbitRotation, intro);
                }
                else
                {
                    classProp.transform.localPosition = Vector3.Lerp(orbitPosition, classPropRestPosition, recovery);
                    classProp.transform.localRotation = Quaternion.Slerp(orbitRotation, classPropRestRotation, recovery);
                }
                yield return null;
            }
            RestoreSwordPose();
            classProp = null;
            swingRoutine = null;
        }

        void RestoreSwordPose()
        {
            if (classProp != null)
                classProp.transform.SetLocalPositionAndRotation(classPropRestPosition, classPropRestRotation);
        }
    }

    /// <summary>
    /// Keeps the authored humanoid arms wrapped around the currently equipped FPS
    /// weapon. This component lives beside the arms Animator so Unity calls its IK pass.
    /// </summary>
    sealed class FirstPersonArmsRig : MonoBehaviour
    {
        enum WeaponGripStyle
        {
            SmallOneHanded,
            LongGunTwoHanded,
            ChainsawTwoHanded,
            MeleeOneHanded
        }

        readonly struct HandPose
        {
            public readonly Vector3 RightPosition;
            public readonly Vector3 RightEuler;
            public readonly Vector3 LeftPosition;
            public readonly Vector3 LeftEuler;
            public readonly bool UsesLeftHand;
            public readonly bool LeftUsesViewSpace;

            public HandPose(Vector3 rightPosition, Vector3 rightEuler,
                bool usesLeftHand, Vector3 leftPosition, Vector3 leftEuler,
                bool leftUsesViewSpace = false)
            {
                RightPosition = rightPosition;
                RightEuler = rightEuler;
                UsesLeftHand = usesLeftHand;
                LeftPosition = leftPosition;
                LeftEuler = leftEuler;
                LeftUsesViewSpace = leftUsesViewSpace;
            }
        }

        static readonly HandPose SmallWeaponPose = new(
            new Vector3(.11f, -.085f, -.075f), new Vector3(-8f, 88f, 92f),
            false, new Vector3(-.23f, -.3f, .18f), new Vector3(12f, -28f, -38f), true);
        static readonly HandPose LongGunPose = new(
            new Vector3(.11f, -.085f, -.075f), new Vector3(-8f, 88f, 92f),
            true, new Vector3(-.02f, -.055f, .24f), new Vector3(8f, -88f, -92f));
        static readonly HandPose ChainsawPose = new(
            new Vector3(.16f, .13f, .39f), new Vector3(-6f, 88f, 88f),
            true, new Vector3(-.02f, .16f, .52f), new Vector3(10f, -88f, -88f));
        static readonly HandPose MeleePose = new(
            new Vector3(.035f, -.035f, .04f), new Vector3(-8f, 88f, 92f),
            false, new Vector3(-.23f, -.3f, .18f), new Vector3(12f, -28f, -38f), true);

        Animator animator;
        PlayerWeaponsManager weapons;
        PlayerArchetype archetype;
        Renderer[] renderers;
        WeaponController cachedWeapon;
        HandPose cachedPose;
        bool renderersVisible = true;

        public void Initialize(Animator targetAnimator, PlayerWeaponsManager manager, PlayerArchetype type,
            Renderer[] armRenderers)
        {
            animator = targetAnimator;
            weapons = manager;
            archetype = type;
            renderers = armRenderers;
        }

        public void SetArchetype(PlayerArchetype type) => archetype = type;

        void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || !animator.isHuman || weapons == null) return;
            var carried=ZombieTown.Foundation.CarryableDefenseItem.LocalCarried;
            if(carried!=null){
                SetRenderersVisible(true);
                float width=carried.isPlank?.4f:.52f;
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand,1);animator.SetIKRotationWeight(AvatarIKGoal.RightHand,1);
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand,1);animator.SetIKRotationWeight(AvatarIKGoal.LeftHand,1);
                animator.SetIKPosition(AvatarIKGoal.RightHand,carried.transform.TransformPoint(new Vector3(width,carried.isPlank?-.06f:-.1f,0)));
                animator.SetIKPosition(AvatarIKGoal.LeftHand,carried.transform.TransformPoint(new Vector3(-width,carried.isPlank?-.06f:-.1f,0)));
                animator.SetIKRotation(AvatarIKGoal.RightHand,carried.transform.rotation*Quaternion.Euler(-8,88,92));
                animator.SetIKRotation(AvatarIKGoal.LeftHand,carried.transform.rotation*Quaternion.Euler(8,-88,-92));
                return;
            }
            WeaponController weapon = weapons.GetActiveWeapon();
            if (weapon == null) return;

            bool useIntegratedHands = weapon.UsesIntegratedFirstPersonHands;
            SetRenderersVisible(!useIntegratedHands);
            if (useIntegratedHands)
            {
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
                animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
                return;
            }

            if (cachedWeapon != weapon)
            {
                cachedWeapon = weapon;
                cachedPose = GetHandPose(weapon);
            }

            Transform grip = weapon.WeaponRoot != null ? weapon.WeaponRoot.transform : weapon.transform;
            Vector3 rightTarget = grip.TransformPoint(cachedPose.RightPosition);
            Quaternion rightRotation = grip.rotation * Quaternion.Euler(cachedPose.RightEuler);

            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
            animator.SetIKPosition(AvatarIKGoal.RightHand, rightTarget);
            animator.SetIKRotation(AvatarIKGoal.RightHand, rightRotation);

            float leftWeight = cachedPose.UsesLeftHand ? 1f : 0f;
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftWeight);
            if (cachedPose.UsesLeftHand)
            {
                Transform leftSpace = cachedPose.LeftUsesViewSpace && weapons.WeaponParentSocket != null
                    ? weapons.WeaponParentSocket : grip;
                animator.SetIKPosition(AvatarIKGoal.LeftHand,
                    leftSpace.TransformPoint(cachedPose.LeftPosition));
                animator.SetIKRotation(AvatarIKGoal.LeftHand,
                    leftSpace.rotation * Quaternion.Euler(cachedPose.LeftEuler));
            }

            Transform view = weapons.WeaponParentSocket;
            if (view == null) return;
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, .85f);
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, leftWeight * .85f);
            animator.SetIKHintPosition(AvatarIKHint.RightElbow,
                view.TransformPoint(new Vector3(.38f, -.48f, -.12f)));
            animator.SetIKHintPosition(AvatarIKHint.LeftElbow,
                view.TransformPoint(new Vector3(-.38f, -.48f, .02f)));
        }

        void SetRenderersVisible(bool visible)
        {
            if (renderersVisible == visible) return;
            renderersVisible = visible;
            if (renderers == null) return;
            foreach (Renderer renderer in renderers)
                if (renderer != null) renderer.enabled = visible;
        }

        HandPose GetHandPose(WeaponController weapon)
        {
            string weaponName = weapon.WeaponName ?? string.Empty;
            WeaponGripStyle style;

            if (weapon.GetComponent<ChainsawWeapon>() != null || NameContains(weaponName, "Chainsaw"))
                style = WeaponGripStyle.ChainsawTwoHanded;
            else if (weapon.IsMeleeWeapon ||
                     (archetype == PlayerArchetype.Striker && NameContains(weaponName, "Sword")))
                style = WeaponGripStyle.MeleeOneHanded;
            else if (IsCompactOneHanded(weaponName))
                style = WeaponGripStyle.SmallOneHanded;
            else if (IsLongGun(weaponName))
                style = WeaponGripStyle.LongGunTwoHanded;
            else
                style = WeaponGripStyle.SmallOneHanded;

            return style switch
            {
                WeaponGripStyle.LongGunTwoHanded => LongGunPose,
                WeaponGripStyle.ChainsawTwoHanded => ChainsawPose,
                WeaponGripStyle.MeleeOneHanded => MeleePose,
                _ => SmallWeaponPose
            };
        }

        static bool IsCompactOneHanded(string weaponName) =>
            NameContains(weaponName, "SMG") ||
            NameContains(weaponName, "MP5") ||
            NameContains(weaponName, "Pistol") ||
            NameContains(weaponName, "Revolver");

        static bool IsLongGun(string weaponName) =>
            NameContains(weaponName, "Rifle") ||
            NameContains(weaponName, "Shotgun") ||
            NameContains(weaponName, "Launcher") ||
            NameContains(weaponName, "Carbine") ||
            NameContains(weaponName, "Machine Gun");

        static bool NameContains(string value, string part) =>
            value.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
