using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Marks an authored opening in a coarse building collider. Projectile hits
    /// inside one of these volumes are ignored while the surrounding wall stays solid.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ProjectilePassThroughVolume : MonoBehaviour
    {
        static readonly HashSet<ProjectilePassThroughVolume> ActiveVolumes = new();
        BoxCollider volume;

        void Awake() => volume = GetComponent<BoxCollider>();

        void OnEnable()
        {
            if (volume == null) volume = GetComponent<BoxCollider>();
            ActiveVolumes.Add(this);
        }

        void OnDisable() => ActiveVolumes.Remove(this);

        public static bool Contains(Vector3 worldPoint)
        {
            foreach (ProjectilePassThroughVolume candidate in ActiveVolumes)
                if (candidate != null && candidate.ContainsPoint(worldPoint)) return true;
            return false;
        }

        bool ContainsPoint(Vector3 worldPoint)
        {
            if (volume == null || !volume.enabled || !gameObject.activeInHierarchy) return false;
            Vector3 point = volume.transform.InverseTransformPoint(worldPoint) - volume.center;
            Vector3 half = volume.size * .5f + Vector3.one * .04f;
            return Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y &&
                   Mathf.Abs(point.z) <= half.z;
        }
    }
}
