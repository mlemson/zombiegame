using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    // Warns the player with a directional message when standing close to a drop-off, so it's harder to
    // accidentally fall off the map while fighting or moving around near an edge.
    public class MapEdgeWarning : MonoBehaviour
    {
        [Tooltip("Horizontal distance ahead of the player, in each direction, probed for a nearby drop-off")]
        public float EdgeCheckDistance = 1.5f;

        [Tooltip("How far down a probe can go before the ground below is considered missing (an edge)")]
        public float EdgeCheckDepth = 20f;

        [Tooltip("Physics layers considered as walkable ground for the edge probes")]
        public LayerMask GroundCheckLayers = -1;

        [Tooltip("How often (in seconds) the surroundings are checked for a nearby edge")]
        public float CheckInterval = 0.25f;

        [Tooltip("Minimum time between warnings, regardless of direction, to avoid spamming the HUD")]
        public float WarningCooldown = 1.75f;

        [Tooltip("How many probes in a direction must find no ground before showing a warning")]
        [Range(1, 5)] public int MinimumMissingProbes = 4;

        // labels are phrased to fit the "Waarschuwing: rand ...!" sentence template
        static readonly (Vector3 LocalDirection, string Label)[] k_Directions =
        {
            (Vector3.forward, "voor je"),
            (new Vector3(1f, 0f, 1f).normalized, "rechtsvoor"),
            (Vector3.right, "rechts"),
            (new Vector3(1f, 0f, -1f).normalized, "rechtsachter"),
            (Vector3.back, "achter je"),
            (new Vector3(-1f, 0f, -1f).normalized, "linksachter"),
            (Vector3.left, "links"),
            (new Vector3(-1f, 0f, 1f).normalized, "linksvoor"),
        };

        CharacterController m_Controller;
        PlayerCharacterController m_PlayerController;
        NetworkObject m_NetworkObject;
        readonly RaycastHit[] m_GroundHits = new RaycastHit[8];
        float m_NextCheckTime;
        float m_LastWarningTime = float.NegativeInfinity;

        void Awake()
        {
            m_Controller = GetComponent<CharacterController>();
            m_PlayerController = GetComponent<PlayerCharacterController>();
            m_NetworkObject = GetComponent<NetworkObject>();
        }

        void Update()
        {
            if (Time.time < m_NextCheckTime)
                return;
            m_NextCheckTime = Time.time + CheckInterval;

            if (m_Controller == null)
                return;

            if (m_PlayerController != null && m_PlayerController.IsDead)
                return;
            if (m_NetworkObject != null && m_NetworkObject.IsSpawned && !m_NetworkObject.IsOwner)
                return;

            Vector3 feet = new(transform.position.x, m_Controller.bounds.min.y + 0.05f, transform.position.z);
            if (!HasWalkableGround(feet + Vector3.up * .5f))
            {
                Vector3 dangerDirection = Vector3.ProjectOnPlane(m_Controller.velocity, Vector3.up);
                if (dangerDirection.sqrMagnitude < .01f) dangerDirection = transform.forward;
                RaiseWarning("onder je", dangerDirection);
                return;
            }
            CheckForNearbyEdge();
        }

        void CheckForNearbyEdge()
        {
            Vector3 feet = new(transform.position.x, m_Controller.bounds.min.y + 0.05f, transform.position.z);

            foreach (var (localDirection, label) in k_Directions)
            {
                Vector3 worldDirection = transform.TransformDirection(localDirection);
                Vector3 sideDirection = Vector3.Cross(Vector3.up, worldDirection).normalized;
                int missingProbes = 0;
                Vector3[] probeOffsets =
                {
                    sideDirection * -1.5f,
                    Vector3.zero,
                    sideDirection * 1.5f,
                    worldDirection * 1.5f,
                    worldDirection * -1.5f
                };
                foreach (Vector3 offset in probeOffsets)
                {
                    Vector3 probeOrigin = feet + worldDirection * EdgeCheckDistance + offset + Vector3.up * .5f;
                    if (!HasWalkableGround(probeOrigin)) missingProbes++;
                }

                if (missingProbes >= Mathf.Clamp(MinimumMissingProbes, 1, probeOffsets.Length))
                {
                    RaiseWarning(label, worldDirection);
                    return;
                }
            }
        }

        bool HasWalkableGround(Vector3 origin)
        {
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, m_GroundHits, EdgeCheckDepth + .5f,
                GroundCheckLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = m_GroundHits[i].collider;
                if (hit != null && !hit.transform.IsChildOf(transform)) return true;
            }
            return false;
        }

        void RaiseWarning(string label, Vector3 dangerDirection)
        {
            if (Time.time - m_LastWarningTime < WarningCooldown)
                return;
            m_LastWarningTime = Time.time;

            EdgeWarningEvent evt = Events.EdgeWarningEvent;
            evt.Message = $"Valgevaar {label}";
            evt.DangerDirection = dangerDirection;
            EventManager.Broadcast(evt);
        }
    }
}
