using UnityEngine;
using Unity.FPS.Gameplay;

namespace ZombieTown.LevelTwo
{
    /// <summary>Presentation-only waypoint revealed after every outpost guard is defeated.</summary>
    [DisallowMultipleComponent]
    public sealed class RadioBeaconMarker : MonoBehaviour
    {
        [SerializeField] Renderer[] markerRenderers;
        [SerializeField] Light[] markerLights;
        [SerializeField] Collider[] markerColliders;
        [SerializeField] ObjectiveReachPoint[] legacyReachObjectives;

        void Awake()
        {
            CachePresentation();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            CachePresentation();
            foreach (Renderer markerRenderer in markerRenderers)
                if (markerRenderer != null) markerRenderer.enabled = visible;
            foreach (Light markerLight in markerLights)
                if (markerLight != null) markerLight.enabled = visible;
            // This object is presentation only. Imported waypoint prefabs can carry an
            // ObjectiveReachPoint and collider, which otherwise register and complete
            // while the beacon is still visually hidden.
            foreach (Collider markerCollider in markerColliders)
                if (markerCollider != null) markerCollider.enabled = false;
            foreach (ObjectiveReachPoint legacyObjective in legacyReachObjectives)
                if (legacyObjective != null) legacyObjective.enabled = false;
        }

        void CachePresentation()
        {
            if (markerRenderers == null || markerRenderers.Length == 0)
                markerRenderers = GetComponentsInChildren<Renderer>(true);
            if (markerLights == null || markerLights.Length == 0)
                markerLights = GetComponentsInChildren<Light>(true);
            if (markerColliders == null || markerColliders.Length == 0)
                markerColliders = GetComponentsInChildren<Collider>(true);
            if (legacyReachObjectives == null || legacyReachObjectives.Length == 0)
                legacyReachObjectives = GetComponentsInChildren<ObjectiveReachPoint>(true);
        }
    }
}
