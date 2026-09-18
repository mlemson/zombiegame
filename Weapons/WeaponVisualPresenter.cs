using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Addons
{
    // Spawns a swappable weapon mesh (and optional optic) under the weapon's WeaponRoot,
    // auto-scaling it to a consistent in-hand size. Generated children are never saved to the prefab.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WeaponController))]
    public sealed class WeaponVisualPresenter : MonoBehaviour
    {
        [Header("Weapon model")]
        public GameObject visualPrefab;
        public Vector3 visualPosition;
        public Vector3 visualEuler;
        public Vector3 visualScale = Vector3.one;

        [Tooltip("The model is uniformly rescaled so its bounds length (local Z) matches this value, in meters.")]
        public float targetWeaponLength = 1f;

        [Tooltip("Local offset used to re-center the model's bounds after rescaling.")]
        public Vector3 targetWeaponCenter;

        [Header("Optic (optional)")]
        [Tooltip("Optional scope/sight prefab. Leave empty to attach none.")]
        public GameObject opticPrefab;
        [Range(0f, 1f)] public float opticLengthRatio = 0.5f;
        public float opticHeightOffset;

        const string VisualInstanceName = "VisualModel";
        const string OpticInstanceName = "OpticModel";

        WeaponController m_Weapon;

        void Awake()
        {
            m_Weapon = GetComponent<WeaponController>();
            Rebuild();
        }

        [ContextMenu("Rebuild Visual")]
        void Rebuild()
        {
            if (m_Weapon == null) m_Weapon = GetComponent<WeaponController>();
            Transform parent = m_Weapon != null && m_Weapon.WeaponRoot != null
                ? m_Weapon.WeaponRoot.transform
                : transform;

            DestroyExisting(parent, VisualInstanceName);
            DestroyExisting(parent, OpticInstanceName);

            if (visualPrefab != null)
            {
                Transform visual = SpawnChild(visualPrefab, parent, VisualInstanceName);
                FitAndPlace(visual);
            }

            if (opticPrefab != null)
            {
                Transform optic = SpawnChild(opticPrefab, parent, OpticInstanceName);
                optic.localPosition = new Vector3(0f, opticHeightOffset, targetWeaponLength * opticLengthRatio);
                optic.localRotation = Quaternion.identity;
            }
        }

        void FitAndPlace(Transform visual)
        {
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0 && targetWeaponLength > 0f)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                float currentLength = Mathf.Max(0.0001f, bounds.size.z);
                float scale = targetWeaponLength / currentLength;
                visual.localScale = Vector3.one * scale;

                Vector3 localCenter = visual.parent.InverseTransformPoint(bounds.center);
                visual.localPosition += targetWeaponCenter - localCenter;
            }

            visual.localPosition += visualPosition;
            visual.localEulerAngles += visualEuler;
            visual.localScale = Vector3.Scale(visual.localScale, visualScale);
        }

        static Transform SpawnChild(GameObject prefab, Transform parent, string childName)
        {
            GameObject instance = Instantiate(prefab, parent);
            instance.name = childName;
            instance.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            return instance.transform;
        }

        static void DestroyExisting(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing == null) return;
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }
    }
}
